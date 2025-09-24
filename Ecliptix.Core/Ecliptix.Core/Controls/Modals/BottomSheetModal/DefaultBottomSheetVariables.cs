using System;
using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public static class DefaultBottomSheetVariables
{
    public const double MinHeight = 10.0;
    public const double MaxHeight = 600.0;
    public const double DefaultWidth = 400.0;

    private const double ContentDelayMs = 40.0;

    public const double DefaultScrimOpacity = 0.15;

    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));
    public static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(ContentDelayMs);

    public const bool DefaultIsDismissableOnScrimClick = true;
}