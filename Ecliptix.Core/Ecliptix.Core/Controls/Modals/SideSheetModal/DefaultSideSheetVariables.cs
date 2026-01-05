using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.SideSheetModal;

public class DefaultSideSheetVariables
{
    public const double DEFAULT_WIDTH = 340.0;
    public const double MAX_WIDTH = 450.0;
    public const double FIXED_HEIGHT = 704.0;

    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));

    public const bool DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK = true;
}
