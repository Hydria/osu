// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mods;
using osuTK.Graphics.OpenGL;

namespace osu.Game.Rulesets.Mania.Difficulty.Skills
{
    public class Strain : StrainDecaySkill
    {
        private const double individual_decay_base = 0.125;
        private const double overall_decay_base = 0.30;
        private const double release_threshold = 30;

        protected override double SkillMultiplier => 1;
        protected override double StrainDecayBase => 1;

        private readonly double[] startTimes;
        private readonly double[] endTimes;
        private readonly double[] individualStrains;

        private double[] noteTimingDifference;
        private double[] noteTimingSorter;
        private double[] noteTimingOrderer;
        private double midpoint = 0;

        private double chordCount = 0;
        private double previousChordCount = 0;

        private double individualStrain;
        private double overallStrain;

        public Strain(Mod[] mods, int totalColumns)
            : base(mods)
        {
            startTimes = new double[totalColumns];
            endTimes = new double[totalColumns];
            individualStrains = new double[totalColumns];
            noteTimingDifference = new double[totalColumns];
            noteTimingSorter = new double[totalColumns];
            noteTimingOrderer = new double[totalColumns];
            overallStrain = 1;
            midpoint = Math.Ceiling(totalColumns / 2.0d);
        }

        protected override double StrainValueOf(DifficultyHitObject current)
        {
            var maniaCurrent = (ManiaDifficultyHitObject)current;
            double startTime = maniaCurrent.StartTime;
            double endTime = maniaCurrent.EndTime;
            int column = maniaCurrent.BaseObject.Column;
            bool isOverlapping = false;

            double closestEndTime = Math.Abs(endTime - startTime); // Lowest value we can assume with the current information
            double holdFactor = 1.0; // Factor to all additional strains in case something else is held
            double holdAddition = 0; // Addition to the current note in case it's a hold and has to be released awkwardly

            for (int i = 0; i < endTimes.Length; ++i)
            {
                // The current note is overlapped if a previous note or end is overlapping the current note body
                isOverlapping |= Precision.DefinitelyBigger(endTimes[i], startTime, 1) &&
                                 Precision.DefinitelyBigger(endTime, endTimes[i], 1) &&
                                 Precision.DefinitelyBigger(startTime, startTimes[i], 1);

                // We give a slight bonus to everything if something is held meanwhile
                if (Precision.DefinitelyBigger(endTimes[i], endTime, 1) &&
                    Precision.DefinitelyBigger(startTime, startTimes[i], 1))
                    holdFactor = 1.25;
                if (!(Math.Abs(startTime - endTimes[i]) <= 1))
                {
                    closestEndTime = Math.Min(Math.Min(closestEndTime, Math.Abs(endTime - endTimes[i])), Math.Abs(startTime - endTimes[i]));
                }
            }

            // The hold addition is given if there was an overlap, however it is only valid if there are no other note with a similar ending.
            // Releasing multiple notes is just as easy as releasing 1. Nerfs the hold addition by half if the closest release is release_threshold away.
            // holdAddition
            //     ^
            // 1.0 + - - - - - -+-----------
            //     |           /
            // 0.5 + - - - - -/   Sigmoid Curve
            //     |         /|
            // 0.0 +--------+-+---------------> Release Difference / ms
            //         release_threshold
            if (isOverlapping)
                holdAddition = DifficultyCalculationUtils.Logistic(x: closestEndTime, multiplier: 0.27, midpointOffset: release_threshold);

            // Decay and increase individualStrains in own column
            individualStrains[column] = applyDecay(individualStrains[column], startTime - startTimes[column], individual_decay_base);
            individualStrains[column] += 2.0 * holdFactor;

            // For notes at the same time (in a chord), the individualStrain should be the hardest individualStrain out of those columns
            individualStrain = maniaCurrent.DeltaTime <= 1 ? Math.Max(individualStrain, individualStrains[column]) : individualStrains[column];

            /* ROLL FACTOR IDEA:
             * WE LOOK AT EACH PRIOR NOTE AND:
             * 1. WE JUDGE THE PREVIOUS NOTE IN THE SAME COLUMN BASED ON HOW FAR AWAY IT WAS IN COMPARISON TO THE OTHER NOTES
             * 2. WE JUDGE HOW MANY HAND SWITCHES HAVE TO BE MADE IN ORDER TO PLAY ALL THE PREVIOUS PATTERNS, SO [1234] IS ONE HAND SWITCH, [1324] IS THREE.
             * NOTE: What do we want as a cutoff, 400ms?
             */

            //work out the ms difference between the current note at the previous note in each column
            for (int i = 0; i < noteTimingDifference.Length; i++) noteTimingDifference[i] = startTime - startTimes[i];
            //in a new array, put them in order
            noteTimingDifference.CopyTo(noteTimingSorter, 0);
            Array.Sort(noteTimingSorter);
            //compare each value in 1st array and designate where it shows up (first instance) in 2nd array
            for (int j = 0; j < noteTimingOrderer.Length; j++)
            {
                if (noteTimingDifference[j] > 400) noteTimingOrderer[j] = 400;
                else noteTimingOrderer[j] = Array.IndexOf(noteTimingSorter, noteTimingDifference[j]); //TODO: make sure the gap between notes isn't too small, ? 20ms?
            }
            //work out the midpoint of the column count (2 for 4K, 3.5 for 7K for example)
            //for examples where it's .5, exclude the next number up as it is a spacebar
            // --- UPDATE --- that's now done at the top

            //view where each number is in the array [0,1,2] and count how many times it switches between the midpoint (max column count) (exclude any above 400ms)
            int currentNote = column;
            bool currentProgressOngoing = true;
            int currentProgress = 0;
            int handSwitches = 0;

            if (noteTimingOrderer[column] > Math.Ceiling(noteTimingDifference.Length / 2.0d)) //we need to limit this check to only the amount of notes featured in the 400ms gap otherwise it causes keymode parity issues
            {
                while (currentProgressOngoing)
                {
                    int arrayIndex = Array.IndexOf(noteTimingOrderer, currentProgress); //TODO: this needs to consider chords
                    if (arrayIndex == -1) break;
                    if (currentNote > midpoint) { if (arrayIndex <= midpoint) handSwitches++; }
                    if (currentNote <= midpoint) { if (arrayIndex > midpoint) handSwitches++; }
                    currentProgress++;
                    currentNote = arrayIndex;
                }
            }
            double rollFactor = Math.Pow(0.75, (currentProgress - handSwitches));

            
            if (maniaCurrent.DeltaTime <= 1)
            { chordCount += 1; }
            else
            {
                previousChordCount = chordCount;
                chordCount = 0;
            }
            // Decay and increase overallStrain
            overallStrain = applyDecay(overallStrain, current.DeltaTime, overall_decay_base) * (Math.Pow(0.997, chordCount + previousChordCount));
            overallStrain += (1 + holdAddition) * holdFactor * rollFactor;

            // Update startTimes and endTimes arrays
            startTimes[column] = startTime;
            endTimes[column] = endTime;

            // By subtracting CurrentStrain, this skill effectively only considers the maximum strain of any one hitobject within each strain section.
            return individualStrain + overallStrain - CurrentStrain;
        }

        protected override double CalculateInitialStrain(double offset, DifficultyHitObject current)
            => applyDecay(individualStrain, offset - current.Previous(0).StartTime, individual_decay_base)
               + applyDecay(overallStrain, offset - current.Previous(0).StartTime, overall_decay_base);

        private double applyDecay(double value, double deltaTime, double decayBase)
            => value * Math.Pow(decayBase, deltaTime / 1000);
    }
}
