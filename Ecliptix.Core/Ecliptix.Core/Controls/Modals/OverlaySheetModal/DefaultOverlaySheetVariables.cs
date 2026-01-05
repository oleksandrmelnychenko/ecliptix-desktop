using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.OverlaySheetModal;

public static class DefaultOverlaySheetVariables
{
    public static readonly double DEFAULT_WIDTH = double.NaN;
    public static readonly double DEFAULT_HEIGHT = double.NaN;

    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));

    public const bool DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK = true;
}
