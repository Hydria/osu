// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Mania.Difficulty.Preprocessing
{
    public class ManiaDifficultyHitObject : DifficultyHitObject
    {
        public new ManiaHitObject BaseObject => (ManiaHitObject)base.BaseObject;

        private readonly List<DifficultyHitObject> objects;
        private readonly HitObject hitObject;

        public ManiaDifficultyHitObject(HitObject hitObject, HitObject lastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
        }

        /// <summary>
        /// Returns the complexity of the current chord based on its bit values.
        /// </summary>
        /// <param name="columns">The key amount.</param>
        /// <returns></returns>
        public double ChordComplexity(int columns)
        {
            bool[] currentChord = new bool[columns + 2];
            double chordComplexity = 0;
            double specialColumn = 0;

            currentChord[((ManiaHitObject)hitObject).Column] = true;
            foreach (ManiaDifficultyHitObject d in objects.Where(o => o.StartTime == StartTime))
                currentChord[d.BaseObject.Column + 1] = true; //sets true if note is present in chord, places it one value down to allow a buffer to avoid issues with upscaling keymodes
            if (columns % 2 != 0)
            {
                //need to remove the middle note as use that as a separate calculation
                if (currentChord[Convert.ToInt32(Math.Ceiling(currentChord.Length / 2.0))])
                {
                    specialColumn = 3;
                }
                //rebuild array without special column
                currentChord.Where((_, i) => i != Math.Ceiling(currentChord.Length / 2.0)).ToArray();
            }

            for (int i = 1; i < columns + 1; i++)
            {
                if (currentChord[i])
                    chordComplexity += 1.0;
                if ((currentChord[i] && !currentChord[i]) || (!currentChord[i] && currentChord[i]))
                    chordComplexity += 1.5;
            }

            return chordComplexity + specialColumn;
        }
    }
}
