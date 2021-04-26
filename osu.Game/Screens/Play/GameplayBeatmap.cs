// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Screens.Play
{
    public class GameplayBeatmap : Component, IBeatmap
    {
        public readonly IBeatmap PlayableBeatmap;

        public GameplayBeatmap(IBeatmap playableBeatmap)
        {
            PlayableBeatmap = playableBeatmap;
        }

        public BeatmapInfo BeatmapInfo
        {
            get => PlayableBeatmap.BeatmapInfo;
            set => PlayableBeatmap.BeatmapInfo = value;
        }

        public BeatmapMetadata Metadata => PlayableBeatmap.Metadata;

        public ControlPointInfo ControlPointInfo
        {
            get => PlayableBeatmap.ControlPointInfo;
            set => PlayableBeatmap.ControlPointInfo = value;
        }

        public List<BreakPeriod> Breaks => PlayableBeatmap.Breaks;

        public double TotalBreakTime => PlayableBeatmap.TotalBreakTime;

        public IReadOnlyList<HitObject> HitObjects => PlayableBeatmap.HitObjects;

        public IEnumerable<BeatmapStatistic> GetStatistics() => PlayableBeatmap.GetStatistics();

        public double GetMostCommonBeatLength() => PlayableBeatmap.GetMostCommonBeatLength();

        public int ClosestSnapTime(double time, int beatDivisor, double? referenceTime = null)
        {
            return PlayableBeatmap.ClosestSnapTime(time, beatDivisor, referenceTime);
        }

        public int ClosestSnapTime(double time, double? referenceTime = null) => PlayableBeatmap.ClosestSnapTime(time, referenceTime);

        public int ClosestBeatDivisor(double time, double? referenceTime = null) => PlayableBeatmap.ClosestBeatDivisor(time, referenceTime);

        public IBeatmap Clone() => PlayableBeatmap.Clone();

        private readonly Bindable<JudgementResult> lastJudgementResult = new Bindable<JudgementResult>();

        public IBindable<JudgementResult> LastJudgementResult => lastJudgementResult;

        public void ApplyResult(JudgementResult result) => lastJudgementResult.Value = result;
    }
}
