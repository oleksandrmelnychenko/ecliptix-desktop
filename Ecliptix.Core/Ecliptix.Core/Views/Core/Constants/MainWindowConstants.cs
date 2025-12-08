using System;

namespace Ecliptix.Core.Views.Core.Constants;

public static class MainWindowConstants
{
    public static class Dimensions
    {
        public const double DEFAULT_WINDOW_WIDTH = 520;
        public const double DEFAULT_WINDOW_HEIGHT = 800;

        public const double AUTH_WINDOW_WIDTH = 532;
        public const double AUTH_WINDOW_HEIGHT = 812;

        public const double MAIN_WINDOW_WIDTH = 1200;
        public const double MAIN_WINDOW_HEIGHT = 800;

        public const double MIN_MAIN_WINDOW_WIDTH = 800;
        public const double MIN_MAIN_WINDOW_HEIGHT = 600;

        public const double MIN_WINDOW_WIDTH = 200;
        public const double MIN_WINDOW_HEIGHT = 300;

        public const double FALLBACK_SCREEN_WIDTH = 1920;
        public const double FALLBACK_SCREEN_HEIGHT = 1080;
    }

    public static class Timing
    {
        public const int ANIMATION_DURATION_MS = 450;
        public const int ANIMATION_FRAME_INTERVAL_MS = 16;
        public const int WINDOW_SNAP_CHECK_DELAY_MS = 200;
        public const int FULL_SCREEN_RESTORE_DELAY_MS = 500;
        public const int FADE_DELAY_MS = 100;
        public const int PLACEMENT_SAVE_THROTTLE_MS = 2000;
        public const int STATE_CHANGE_DEBOUNCE_MS = 100;
    }

    public static class Layout
    {
        public const int SNAP_DETECTION_TOLERANCE = 10;
        public const int WINDOW_REPOSITION_OFFSET = 1;
        public const double EASING_THRESHOLD = 0.5;
        public const double ANIMATION_PROGRESS_COMPLETE = 1.0;
        public const double CENTER_DIVISOR = 2.0;
        public const double DRAG_THRESHOLD = 3.0;
    }

    public static class SnapFractions
    {
        public const double QUARTER = 0.25;
        public const double THIRD = 1.0 / 3.0;
        public const double TWO_THIRDS = 2.0 / 3.0;
        public const double HALF = 0.5;
        public const double FULL = 1.0;
    }

    public static class TimeSpans
    {
        public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(Timing.ANIMATION_DURATION_MS);

        public static readonly TimeSpan AnimationFrameInterval =
            TimeSpan.FromMilliseconds(Timing.ANIMATION_FRAME_INTERVAL_MS);

        public static readonly TimeSpan WindowSnapCheckDelay =
            TimeSpan.FromMilliseconds(Timing.WINDOW_SNAP_CHECK_DELAY_MS);

        public static readonly TimeSpan FullScreenRestoreDelay =
            TimeSpan.FromMilliseconds(Timing.FULL_SCREEN_RESTORE_DELAY_MS);

        public static readonly TimeSpan FadeDelay = TimeSpan.FromMilliseconds(Timing.FADE_DELAY_MS);

        public static readonly TimeSpan PlacementSaveThrottle =
            TimeSpan.FromMilliseconds(Timing.PLACEMENT_SAVE_THROTTLE_MS);

        public static readonly TimeSpan
            StateChangeDebounce = TimeSpan.FromMilliseconds(Timing.STATE_CHANGE_DEBOUNCE_MS);
    }
}
