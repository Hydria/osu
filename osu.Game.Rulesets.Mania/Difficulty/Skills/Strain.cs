// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Native.Windows.Input;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mania.Difficulty.Skills
{
    public class Strain : StrainDecaySkill
    {
        private const double individual_decay_base = 0.125;
        private const double overall_decay_base = 0.30;
        private const double release_threshold = 24;

        protected override double SkillMultiplier => 1;
        protected override double StrainDecayBase => 1;

        private readonly double[] startTimes;
        private readonly double[] endTimes;
        private readonly double[] individualStrains;

        private double individualStrain;
        private double overallStrain;

        public Strain(Mod[] mods, int totalColumns)
            : base(mods)
        {
            startTimes = new double[totalColumns];
            endTimes = new double[totalColumns];
            individualStrains = new double[totalColumns];
            overallStrain = 1;
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
                isOverlapping |= Precision.DefinitelyBigger(endTimes[i], startTime, 1) && Precision.DefinitelyBigger(endTime, endTimes[i], 1);

                // We give a slight bonus to everything if something is held meanwhile
                if (Precision.DefinitelyBigger(endTimes[i], endTime, 1))
                    holdFactor = 1.25;

                closestEndTime = Math.Min(closestEndTime, Math.Abs(endTime - endTimes[i]));
            }
            // --- OKAY LETS START BY SOLVING THE CHORD / JUMPTRILL ISSUE --- //
            //find the last note used
            double[] lastNoteList = startTimes.Where(c => c != startTime).ToArray();
            double lastNote = lastNoteList.Max();

            //see what columns use that note (chord checker)
            int[] columnTracker = new int[startTimes.Length];
            int columnTrackerPlacement = 0;
            int columnPlacement = 1;
            foreach (double timingPoint in startTimes) { if (timingPoint == lastNote) { columnTracker[columnTrackerPlacement] = columnPlacement; columnTrackerPlacement++; } columnPlacement++; }

            //remove all null values from array
            columnTracker = columnTracker.Where(c => c != 0).ToArray();

            //since i had to increase the col value by 1, time to reduce them all again
            columnTrackerPlacement = 0;
            foreach (int timingPoint in columnTracker) { columnTracker[columnTrackerPlacement] = timingPoint - 1; columnTrackerPlacement++; }

            //figure out the longest run of numbers (aka how mashable is the chord) (spits out higher numbers for --xxxx- than it would x-xx--x)
            //this is completely unedited stolen code from the internet
            HashSet<int> values = new HashSet<int>(columnTracker);

            double bestLength = 0;
            // Can't use foreach as we're modifying it in-place
            while (values.Count > 0)
            {
                int value = values.First();
                values.Remove(value);
                int start = value;
                while (values.Remove(start - 1)) { start--; }

                int end = value;
                while (values.Remove(end + 1)) { end++; }

                int length = end - start + 1;
                if (length > bestLength) { bestLength = length; }
            }

            //lets calculate chord difficulty:
            //chord count:
            double chordSize = columnTracker.Length;
            //chord calculation:
            double chordDifficulty = Math.Pow(0.8, (Math.Cos((2.2 * chordSize) / (Convert.ToDouble(startTimes.Length) / 2.0d)) + ((Convert.ToDouble(startTimes.Length) / 2.0d) / (1 + Math.Exp(10 * (Convert.ToDouble(startTimes.Length) - (chordSize - 0.1)))))));

            //introduce a multiplication nerf based on how many notes are close together and if they're all on one hand
            //bestLength will always return 1 so we add +1 to the amount of cols to start
            double biasReductionValue = ((Convert.ToDouble(startTimes.Length) + (1.0d / 2.0d) - (bestLength / 2.0d)) / Convert.ToDouble(startTimes.Length));
            //add in the complexity of the chord:
            biasReductionValue *= chordDifficulty - (1 - chordDifficulty);
            //if the next note is nearby we dont want to reduce difficulty too much
            double timeBetweenNotes = startTime - lastNote;
            biasReductionValue *= (.5 + (1 / (2 + Math.Exp(0.2 * (timeBetweenNotes - 60)))));
            biasReductionValue = (1.0d / 3.0d) + (biasReductionValue / 1.5);
            // --- OKAY NOW THAT'S SORTED, LETS DEAL WITH ROLLS (THE BIGGER HEADACHE) --- //
            //gonna say that a roll has to be over at least 4 columns (2 on each hand), a 3 note roll in this context is literally just gonna be any stream

            //first of all, how many notes has there been since the last note in this column
            double lastNoteTiming = startTimes[column];
            double[] inBetweenNotes = startTimes.Where(c => c >= lastNoteTiming && c < startTime).ToArray();
            //the length of the above array will be the beginning of the calculations

            //next of all lets calculate what it takes to chordtrill
            //43ms is T&R, 59ms is Cry... 50ms cutoff? that's like 300bpm chordtrill or 600bpm streams [covers 4 notes]
            //if anyone's hitting 600bpm tech streams legit we'll revisit this
            double chordTrillNerf = 1;
            double HalfColumnCount = (Math.Floor(startTimes.Length / 2.0d));
            bool leftHandChordTrill = false;
            bool rightHandChordTrill = false;
            bool leftHandChord = false;
            bool rightHandChord = false; //need to make sure im not just checking chords (which will return true on "is this a trillable chord")
            double startingValue = 0;
            //okay lets see if there are enough previous notes to even warrant checking
            if (inBetweenNotes.Length >= HalfColumnCount * 2.0d) //the /2, flooring and *2 makes odd number keys require one key less (for spacebar reasons)
            {
                //okay lets check each hand to see if all the notes in that hand are close enough to warrant classing as a chord
                leftHandChordTrill = true;
                leftHandChord = true;
                rightHandChordTrill = true;
                rightHandChord = true;
                for (int i = 0; i < Math.Ceiling(startTimes.Length / 2.0d); i++)
                {
                    if (startingValue == 0) { startingValue = startTimes[i]; }
                    else if (startTimes[i] > startingValue + 50 || startTimes[i] < startingValue - 50) { leftHandChordTrill = false; }
                    else if (startTimes[i] != startingValue) { leftHandChord = false; }
                }
                startingValue = 0; //lets reset values again
                for (int i = Convert.ToInt32(HalfColumnCount); i < startTimes.Length; i++)
                {
                    if (startingValue == 0) { startingValue = startTimes[i]; }
                    else if (startTimes[i] > startingValue + 50 || startTimes[i] < startingValue - 50) { rightHandChordTrill = false; }
                    else if (startTimes[i] != startingValue) { rightHandChord = false; }
                }
            }
            //chord check to stop chord nerf (that's fixed above)
            //if either value is true, we reduce the overall strain by an amount respective to the amount of keys being pressed (i.e. for 4K that's 50% each hand, for 7K that's down to 33% for each hand)
            if (leftHandChordTrill && !leftHandChord) { chordTrillNerf *= (1 / HalfColumnCount); }
            if (rightHandChordTrill && !rightHandChord) { chordTrillNerf *= (1 / HalfColumnCount); }
            //nerfing the overall value by up to 98.8% isn't going to show very high sr so lets bring that up a bit, using 7K as a rough estimate (aka 11%)
            //rough values we have are:
            //4K -> 0.25 / 0.50 / 1 -> 0.625 / 0.75 / 1
            //6K -> 0.11 / 0.33 / 1 -> 0.555 / 0.66 / 1
            //8K -> 0.06 / 0.25 / 1 -> 0.530 / 0.63 / 1
            chordTrillNerf = (1.0d / 3.0d) + (chordTrillNerf / 1.5);

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
                holdAddition = 1 / (1 + Math.Exp(0.5 * (release_threshold - closestEndTime)));

            // Decay and increase individualStrains in own column
            individualStrains[column] = applyDecay(individualStrains[column], startTime - startTimes[column], individual_decay_base, false);
            individualStrains[column] += 2.0 * holdFactor;

            // For notes at the same time (in a chord), the individualStrain should be the hardest individualStrain out of those columns
            individualStrain = maniaCurrent.DeltaTime <= 1 ? Math.Max(individualStrain, individualStrains[column]) : individualStrains[column];

            // Decay and increase overallStrain
            overallStrain = applyDecay(overallStrain, current.DeltaTime, overall_decay_base, true, (biasReductionValue * chordTrillNerf));
            overallStrain += (1 + holdAddition) * holdFactor;

            // Update startTimes and endTimes arrays
            startTimes[column] = startTime;
            endTimes[column] = endTime;

            // By subtracting CurrentStrain, this skill effectively only considers the maximum strain of any one hitobject within each strain section.
            return individualStrain + overallStrain - CurrentStrain;
        }

        protected override double CalculateInitialStrain(double offset, DifficultyHitObject current)
            => applyDecay(individualStrain, offset - current.Previous(0).StartTime, individual_decay_base, false)
               + applyDecay(overallStrain, offset - current.Previous(0).StartTime, overall_decay_base, false);

        private double applyDecay(double value, double deltaTime, double decayBase, bool strainReduction, double ChordCalulation = 1)
        {
            if (strainReduction == true) { return value * (Math.Pow(decayBase, deltaTime / 1000.0d) - Math.Pow(0.95, 175 * ChordCalulation)); }
            else { return value * Math.Pow(decayBase, deltaTime / 1000.0d); }
        }
    }
}
