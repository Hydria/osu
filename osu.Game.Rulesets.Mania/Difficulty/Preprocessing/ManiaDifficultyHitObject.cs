// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

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
            this.objects = objects;
            this.hitObject = hitObject;
        }

        /// <summary>
        /// Returns the bool value of the current chord (true if there is a note in the column at that time).
        /// </summary>
        /// <param name="columns">The key amount.</param>
        /// <returns></returns>
        public bool[] ChordValue(int columns)
        {
            bool[] currentChord = new bool[columns];
            currentChord[((ManiaHitObject)hitObject).Column] = true;
            foreach (ManiaDifficultyHitObject d in objects.Where(o => o.StartTime == StartTime))
                currentChord[d.BaseObject.Column] = true;
            return currentChord;
        }
    }
}
