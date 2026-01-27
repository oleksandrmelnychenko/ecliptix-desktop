using Avalonia;
using Avalonia.Media;
using Ecliptix.Core.Controls.Core;

namespace Ecliptix.Core.Controls.Configuration;

public class SegmentConfiguration : AvaloniaObject
{
    public static readonly StyledProperty<double> WidthProperty =
        AvaloniaProperty.Register<SegmentConfiguration, double>(nameof(Width), 40.0);

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<SegmentConfiguration, double>(nameof(Spacing), 8.0);

    public static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<SegmentConfiguration, IBrush>(nameof(Background), Brushes.Transparent);

    public static readonly StyledProperty<IBrush> BorderBrushProperty =
        AvaloniaProperty.Register<SegmentConfiguration, IBrush>(nameof(BorderBrush), Brushes.Transparent);

    public static readonly StyledProperty<IBrush> ActiveBorderBrushProperty =
        AvaloniaProperty.Register<SegmentConfiguration, IBrush>(nameof(ActiveBorderBrush), Brushes.Blue);

    public static readonly StyledProperty<SegmentBorderStyle> BorderStyleProperty =
        AvaloniaProperty.Register<SegmentConfiguration, SegmentBorderStyle>(nameof(BorderStyle),
            SegmentBorderStyle.Box);

    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<SegmentConfiguration, CornerRadius>(nameof(CornerRadius), new CornerRadius(0));

    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public double Width
    {
        get => GetValue(WidthProperty);
        set => SetValue(WidthProperty, value);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public IBrush Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public IBrush ActiveBorderBrush
    {
        get => GetValue(ActiveBorderBrushProperty);
        set => SetValue(ActiveBorderBrushProperty, value);
    }

    public SegmentBorderStyle BorderStyle
    {
        get => GetValue(BorderStyleProperty);
        set => SetValue(BorderStyleProperty, value);
    }
}
