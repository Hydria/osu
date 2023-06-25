// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using osu.Framework.Utils;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
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
        private readonly bool[] chordCurrent;

        private double individualStrain;
        private double overallStrain;
        private double chordDifficulty;

        public Strain(Mod[] mods, int totalColumns)
            : base(mods)
        {
            startTimes = new double[totalColumns];
            endTimes = new double[totalColumns];
            individualStrains = new double[totalColumns];
            chordCurrent = new bool[totalColumns + 2];
            overallStrain = 1;
            chordDifficulty = 0;
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

            // Reset the chord if the current note doesn't start at the same time and then fill it with all relevant notes
            if (Precision.DefinitelyBigger(current.DeltaTime, 0, 1))
            {
                Array.Fill(chordCurrent, false);
                chordCurrent[column] = true;

                int i = 0;
                var nextNote = (ManiaDifficultyHitObject)current.Next(0);
                if (nextNote != null) // Checks to see if we're not at the end of the file
                {
                    double nextNoteStartTime = nextNote.StartTime;

                    // Checks to see if next note is a chord relative to current note
                    while (Precision.AlmostEquals(nextNoteStartTime, startTime, 1) && nextNote != null)
                    {
                        // Changes chord column to true, avoids overinflation with stacked notes
                        chordCurrent[nextNote.BaseObject.Column] = true;
                        i++;
                        nextNote = (ManiaDifficultyHitObject)current.Next(i);
                        if (nextNote != null) nextNoteStartTime = nextNote.StartTime;
                    }
                }
                // Figure out the chord complexity of the current chord and divide the value by how many notes are present
                // Since we only need to run this on every chord update it's done in here
                chordDifficulty = chordComplexity(chordCurrent) / chordCurrent.Count(c => c);
            }

            for (int i = 0; i < endTimes.Length; ++i)
            {
                // The current note is overlapped if a previous note or end is overlapping the current note body
                isOverlapping |= Precision.DefinitelyBigger(endTimes[i], startTime, 1) && Precision.DefinitelyBigger(endTime, endTimes[i], 1);

                // We give a slight bonus to everything if something is held meanwhile
                if (Precision.DefinitelyBigger(endTimes[i], endTime, 1))
                    holdFactor = 1.25;

                closestEndTime = Math.Min(closestEndTime, Math.Abs(endTime - endTimes[i]));
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
                holdAddition = 1 / (1 + Math.Exp(0.5 * (release_threshold - closestEndTime)));

            // Decay and increase individualStrains in own column
            individualStrains[column] = applyDecay(individualStrains[column], startTime - startTimes[column], individual_decay_base);
            individualStrains[column] += 2.0 * holdFactor;

            // For notes at the same time (in a chord), the individualStrain should be the hardest individualStrain out of those columns
            individualStrain = maniaCurrent.DeltaTime <= 1 ? Math.Max(individualStrain, individualStrains[column]) : individualStrains[column];

            // Decay and increase overallStrain
            overallStrain = applyDecay(overallStrain, current.DeltaTime, overall_decay_base);
            overallStrain += (1 + holdAddition + chordDifficulty) * holdFactor;

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

        private double chordComplexity(bool[] chordCurrent)
        {
            int columns = chordCurrent.Length;
            double chordComplexity = 0;
            double specialColumn = 0;

            if (columns % 2 != 0)
            {
                // Work out the special column placement.
                int specialColumnIndex = (int)Math.Ceiling(columns / 2.0);

                // Need to remove the middle note as we use that as a separate calculation, so calculate its worth here.
                if (chordCurrent[specialColumnIndex])
                    specialColumn = 3;

                // Rebuild array without special column.
                chordCurrent.Where((_, i) => i != specialColumnIndex).ToArray();

                // Recalculate columns value.
                columns = chordCurrent.Length;
            }

            for (int i = 0; i < columns - 1; i++)
            {
                if (chordCurrent[i]) // If the note exists, count it.
                    chordComplexity += 1.0;
                if ((chordCurrent[i] && !chordCurrent[i + 1]) || (!chordCurrent[i] && chordCurrent[i + 1])) // If the current bool is different to the next one, count it.
                    chordComplexity += 1.5;
            }

            return chordComplexity + specialColumn;
        }
    }
}
