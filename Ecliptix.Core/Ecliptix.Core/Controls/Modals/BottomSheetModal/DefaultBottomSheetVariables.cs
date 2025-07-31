using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public static class DefaultBottomSheetVariables
{
    public const double MinHeight = 200.0;
    public const double MaxHeight = 600.0;
    public const double DefaultAppearVerticalOffset = 0.0;
    public const double DefaultDisappearVerticalOffset = 0.0;
    public const double DefaultOpacity = 0.5;
    public const double DefaultToOpacity = 1.0;
    public const double DefaultAnimationDuration = 300.0;
    public static readonly IBrush DefaultScrimColor = Brushes.Black;
    public const bool DefaultIsDismissableOnScrimClick = true;
    public static readonly IBrush DefaultDismissableScrimColor = Brushes.Black;
    public static readonly IBrush DefaultUnDismissableScrimColor = Brushes.Gray;
    public const double DefaultScrimOpacity = 0.1;
}