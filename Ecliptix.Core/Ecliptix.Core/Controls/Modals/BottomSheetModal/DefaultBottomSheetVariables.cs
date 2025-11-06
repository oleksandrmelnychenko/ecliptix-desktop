using System;
using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public static class DefaultBottomSheetVariables
{
    public const double MIN_HEIGHT = 10.0;
    public const double MAX_HEIGHT = 600.0;
    public const double DEFAULT_WIDTH = 400.0;

    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));

    public const bool DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK = true;
}
