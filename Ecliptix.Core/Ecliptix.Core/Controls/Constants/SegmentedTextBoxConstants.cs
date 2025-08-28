namespace Ecliptix.Core.Controls.Constants;

public static class SegmentedTextBoxConstants
{
    // CSS class names
    public const string SegmentStyleClass = "segment";
    public const string ActiveStyleClass = "active";

    // Default values
    public const int DefaultSegmentCount = 4;
    public const double DefaultSegmentSpacing = 8.0;
    public const double DefaultSegmentWidth = 40.0;
    public const bool DefaultAllowOnlyNumbers = false;
    public const bool IsPointerInteractionEnabled = true; // Fixed typo from IsPonterInteractionEnabled

    // Layout values
    public const double DefaultSegmentMargin = 4.0;
    public const int MaxLengthPerSegment = 1;
    public const double DefaultCornerRadius = 5.0;
    public const double DefaultBorderThickness = 0.0;
    public const double ActiveBorderThickness = 1.0;

    // Control names
    public const string SegmentsPanelName = "SegmentsPanel";
    public const string OverlayRectangleName = "OverlayRectangle";

    // Shadow values
    public const string FocusShadowValue = "0 0 10 4 #406a5acd, 0 0 0 0 #20000000";

    // Z-Index values
    public const int OverlayZIndex = 1;
}