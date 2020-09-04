// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Input.Bindings;
using osu.Game.Beatmaps;
using osu.Game.Input.Bindings;
using osu.Game.Overlays.OSD;

namespace osu.Game.Overlays.Music
{
    /// <summary>
    /// Handles <see cref="GlobalAction"/> relating to music playback, and displays a <see cref="Toast"/> via the cached <see cref="OnScreenDisplay"/> accordingly.
    /// </summary>
    public class MusicActionHandler : Component, IKeyBindingHandler<GlobalAction>
    {
        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; }

        [Resolved]
        private MusicController musicController { get; set; }

        [Resolved]
        private OnScreenDisplay onScreenDisplay { get; set; }

        public bool OnPressed(GlobalAction action)
        {
            if (beatmap.Disabled)
                return false;

            switch (action)
            {
                case GlobalAction.MusicPlay:
                    if (musicController.TogglePause())
                        onScreenDisplay.Display(new MusicActionToast(musicController.IsPlaying ? "Play track" : "Pause track"));

                    return true;

                case GlobalAction.MusicNext:
                    musicController.NextTrack(() =>
                    {
                        onScreenDisplay.Display(new MusicActionToast("Next track"));
                    }).RunTask();

                    return true;

                case GlobalAction.MusicPrev:
                    musicController.PreviousTrack(res =>
                    {
                        switch (res)
                        {
                            case PreviousTrackResult.Restart:
                                onScreenDisplay.Display(new MusicActionToast("Restart track"));
                                break;

                            case PreviousTrackResult.Previous:
                                onScreenDisplay.Display(new MusicActionToast("Previous track"));
                                break;
                        }
                    }).RunTask();

                    return true;
            }

            return false;
        }

        public void OnReleased(GlobalAction action)
        {
        }

        private class MusicActionToast : Toast
        {
            public MusicActionToast(string action)
                : base("Music Playback", action, string.Empty)
            {
            }
        }
    }
}
