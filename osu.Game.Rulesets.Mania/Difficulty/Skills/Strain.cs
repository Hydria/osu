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
            double lastNote = startTimes.Max();

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

            //figure out if all the notes happen on one hand or two hands
            bool handBias = false;
            if (!(columnTracker[0] <= ((Convert.ToDouble(startTimes.Length) + 1) / 2) && columnTracker[columnTracker.Length - 1] >= ((Convert.ToDouble(startTimes.Length) + 1) / 2))) { handBias = true; }
            if (columnTracker.Length == 1) { handBias = false; }

            //lets calculate chord difficulty:
            //chord count:
            double chordSize = columnTracker.Length;
            //chord calculation:
            double chordDifficulty = Math.Pow(0.9, (Math.Cos((2.2 * chordSize) / (Convert.ToDouble(startTimes.Length) / 2)) + ((Convert.ToDouble(startTimes.Length) / 2) / (1 + Math.Exp(10 * (Convert.ToDouble(startTimes.Length) - (chordSize - 0.1)))))));

            //introduce a multiplication nerf based on how many notes are close together and if they're all on one hand
            //bestLength will always return 1 so we add +1 to the amount of cols to start
            double biasReductionValue = ((Convert.ToDouble(startTimes.Length) + (1 / 12) - (bestLength / 12)) / Convert.ToDouble(startTimes.Length));
            //add in the complexity of the chord:
            biasReductionValue *= chordDifficulty - (1 - chordDifficulty);
            //hand value
            if (handBias == true) { if (Convert.ToDouble(columnTracker.Length) <= ((Convert.ToDouble(startTimes.Length) + 1) / 2) || Convert.ToDouble(columnTracker.Length) >= (Convert.ToDouble(startTimes.Length) / 2)) { biasReductionValue *= 0.85; } else { biasReductionValue *= 0.95; } }

            //lets add a little bonus if the pattern is [5]->[4][6] for example
            double[] startTimeOrder = startTimes.OrderByDescending(c => c).Distinct().ToArray();
            if (startTimeOrder.Length > 1)
            {
                int noteLocation = Array.IndexOf(startTimes, startTimeOrder[1]);
                if (noteLocation != 0 && noteLocation != startTimes.Length - 1)
                {
                    if (startTimes[noteLocation + 1] == lastNote && startTimes[noteLocation - 1] == lastNote) { biasReductionValue *= 1 + (1 / (1 + Math.Exp(0.025 * (startTimeOrder[0] - startTimeOrder[1])))); }
                }
            }

            // --- OKAY NOW THAT'S SORTED, LETS DEAL WITH ROLLS (THE BIGGER HEADACHE) --- //
            //gonna say that a roll has to be over at least 4 columns (2 on each hand), a 3 note roll in this context is literally just gonna be any stream

            //first of all, how many notes has there been since the last note in this column
            double lastNoteTiming = startTimes[column];
            double[] inBetweenNotes = startTimes.Where(c => c > lastNoteTiming).ToArray();
            //the length of the above array will be the beginning of the calculations

            //second of all, what has been the distance (aka absolute value) between these notes on a per hand basis (aka 1-3-2 on the left hand is +3 while 1-2-3 on the left hand is +2)
            //the maximum gap between notes is (((Math.Floor(startTimes.Length) / 2) - 1) * (Math.Floor(startTimes.Length) / 2)) / 2

            //sort the array so we know what order the numbers come in
            Array.Sort(inBetweenNotes);
            //make an array which stores the value location of each of the notes in the array
            double[] noteLocations = new double[startTimes.Length];
            int j = 0;

            //annoyingly, before we start, i need to convert the doubles into ints for IndexOf to work
            int[] inBetweenInts = new int[inBetweenNotes.Length];
            int[] startInt = new int[startTimes.Length];

            for (int k = 0; k < inBetweenNotes.Length; k++) { inBetweenInts[k] = Convert.ToInt32(Math.Floor(inBetweenNotes[k])); }
            for (int k = 0; k < startTimes.Length; k++) { startInt[k] = Convert.ToInt32(Math.Floor(startTimes[k])); }

            foreach (int note in startInt)
            {
                if (note <= lastNoteTiming) { noteLocations[j] = 0; }
                else { noteLocations[j] = Array.IndexOf(inBetweenInts, note) + 1; }
                j++;
            }

            //split the array in half to work out hand-based calculations
            double arrayLength = noteLocations.Length;
            double[] leftHandArray = noteLocations.Take(Convert.ToInt32(Math.Floor(arrayLength / 2))).ToArray();
            double[] rightHandArray = noteLocations.Skip(Convert.ToInt32(Math.Ceiling(arrayLength / 2))).ToArray(); //[1][3][3][6] to [1][2][2][3]

            int noteTracker = 1;
            for (int k = 1; k <= startTimes.Length; k++)
            {
                bool isChord = false;
                for (int m = 0; m < leftHandArray.Length; m++)
                {
                    if (leftHandArray[m] == k)
                    {
                        leftHandArray[m] = noteTracker;
                        if (isChord == true) { leftHandArray[m]--; }
                        if (isChord == false) { isChord = true; noteTracker++; }
                    }
                }
            }

            noteTracker = 1;
            for (int k = 1; k <= startTimes.Length; k++)
            {
                bool isChord = false;
                for (int m = 0; m < rightHandArray.Length; m++)
                {
                    if (rightHandArray[m] == k)
                    {
                        rightHandArray[m] = noteTracker;
                        if (isChord == true) { rightHandArray[m]--; }
                        if (isChord == false) { isChord = true; noteTracker++; }
                    }
                }
            }

            //count the absolute distance between notes
            double leftHandValue = 0;
            double rightHandValue = 0;
            double bothHandValue = 0;
            int lastNotePosition = 0;
            int currentNote = 0;
            double leftHandCount = 0;
            double rightHandCount = 0;
            double bothHandCount = 0;
            double noteDistancePercentage = 1;

            foreach (double note in leftHandArray)
            {
                if (note != 0)
                {
                    if (lastNotePosition == 0) { lastNotePosition = currentNote; }
                    else { leftHandValue += Math.Abs((note - leftHandArray[lastNotePosition])); }
                    leftHandCount++;
                }
                currentNote++;
            }
            currentNote = 0;
            lastNotePosition = 0; //reset values for 2nd check
            foreach (double note in rightHandArray)
            {
                if (note != 0)
                {
                    if (lastNotePosition == 0) { lastNotePosition = currentNote; }
                    else { rightHandValue += Math.Abs((note - rightHandArray[lastNotePosition])); }
                    rightHandCount++;
                }
                currentNote++;
            }
            currentNote = 0;
            lastNotePosition = 0; //reset values for 3rd check
            foreach (double note in noteLocations)
            {
                if (note != 0)
                {
                    if (lastNotePosition == 0) { lastNotePosition = currentNote; }
                    else { bothHandValue += Math.Abs((note - noteLocations[lastNotePosition])); }
                    bothHandCount++;
                }
                currentNote++;
            }

            //calculate how far from the max value the notes are away from each other
            //the further apart they are, the more manip they are
            double leftHandPercentage =  leftHandValue /  Math.Max(((leftHandCount  * (leftHandCount - 1))  / 2) + ((startTimes.Length) - 1), 1);
            double rightHandPercentage = rightHandValue / Math.Max(((rightHandCount * (rightHandCount - 1)) / 2) + ((startTimes.Length) - 1), 1);
            double bothHandPercentage = bothHandValue / Math.Max(((bothHandCount * (bothHandCount - 1)) / 2) + ((startTimes.Length) - 1), 1);
            if (leftHandPercentage == 0) leftHandPercentage = 1; //these deal with the note being a jack
            if (rightHandPercentage == 0) rightHandPercentage = 1;
            if (leftHandPercentage == 1 && rightHandPercentage == 1) { noteDistancePercentage = 1; }
            else if (leftHandPercentage == 1) { noteDistancePercentage = rightHandPercentage; }
            else if (rightHandPercentage == 1) { noteDistancePercentage = leftHandPercentage; }
            else { noteDistancePercentage = (leftHandPercentage + rightHandPercentage) / 2; }

            if (bothHandPercentage == 0) { noteDistancePercentage  = 1; }
            else { noteDistancePercentage = (0.5 + ((noteDistancePercentage + bothHandPercentage) / 4)); }

            //third of all, how close are the notes? does it seem fast enough to manip or are they just far apart
            //we should go with a pretty fast falloff in terms of nerfing difficulty, if we use 180BPM (as does the RC) in terms of a 4 note roll, that means the gap is ~42ms between each note.
            //starting the nerf curve at 16ms as well as a standard "this is just a chord" value
            double totalNoteDistance = 0;
            double totalNotes = 0;
            double averageNoteDistance = 0;
            foreach (double note in inBetweenNotes)
            {
                if (inBetweenNotes.Length == 0) { }
                else
                {
                    totalNoteDistance += startTime - note;
                    totalNotes++;
                }
            }
            if (inBetweenNotes.Length != 0) { averageNoteDistance = totalNoteDistance / totalNotes; }

            if (averageNoteDistance == 0) { averageNoteDistance = startTime - startTimes[column]; }

            double overallRollNerf = 0.8 + (((Convert.ToDouble(startTimes.Length - inBetweenNotes.Length) / Convert.ToDouble(startTimes.Length)) * noteDistancePercentage * (1 / (1 + Math.Exp(0.3 * (29 - averageNoteDistance))))) / 5);
            overallRollNerf *= 0.75;
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
            overallStrain = applyDecay(overallStrain, current.DeltaTime, overall_decay_base, true, biasReductionValue * overallRollNerf);
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
            if (strainReduction == true) { return value * (Math.Pow(decayBase, deltaTime / 1000) - Math.Pow(0.95, (15 * deltaTime) + ((ChordCalulation / 2) * 300))); }
            else { return value * Math.Pow(decayBase, deltaTime / 1000); }
        }
    }
}
