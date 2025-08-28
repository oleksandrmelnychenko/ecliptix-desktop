namespace Ecliptix.Core.Controls.Constants;

public static class NetworkStatusConstants
{
    // Timing durations (in milliseconds)
    public const int DefaultAppearDurationMs = 300;
    public const int DefaultDisappearDurationMs = 250;
    public const int StatusUpdateDelayMs = 200;
    public const int AutoHideDelayMs = 2000;

    // Asset URIs
    public const string NoInternetIconUri = "avares://Ecliptix.Core/Assets/Icons/Network/wifi.png";
    public const string ServerNotRespondingIconUri = "avares://Ecliptix.Core/Assets/Icons/Network/ServerShutdownAmber30x30.png";

    // Colors
    public const string TransparentColorHex = "#00000000";
    public const string ScrimColorHex = "#80000000";

    // Animation properties
    public const double DefaultOpacityHidden = 0.0;
    public const double DefaultOpacityVisible = 1.0;
    public const double DefaultTransformOffsetY = 100.0;
    public const double DefaultTransformOffsetYVisible = 0.0;

    // Control names
    public const string MainBorderName = "MainBorder";
    public const string ScrimBorderName = "ScrimBorder";
    public const string RootGridName = "RootGrid";
    public const string ContentControlName = "ContentControl";

    // Animation easing
    public const string DefaultEasingName = "CubicEaseOut";
}