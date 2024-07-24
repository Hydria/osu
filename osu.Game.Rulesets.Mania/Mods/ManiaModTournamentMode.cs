// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Mania.Mods
{
    public partial class ManiaModTournamentMode : Mod, IApplicableAfterBeatmapConversion
    {
        public override string Name => "Tournament Mode";

        public override string Acronym => "TM";

        public override double ScoreMultiplier => 1.0;

        public override LocalisableString Description => @"Always Enabled. Client Modification.";

        public override IconUsage? Icon => FontAwesome.Solid.FlagCheckered;

        public override ModType Type => ModType.DifficultyIncrease;

        public void ApplyToBeatmap(IBeatmap beatmap)
        {

        }
    }
}
