using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Ecliptix.Core.Controls.Common;

namespace Ecliptix.Core.Controls.Core.Icons;

public class EcliptixIcon : IconElement
{
    public EcliptixIcon()
    {
        AffectsRender<EcliptixIcon>([KindProperty,StrokeProperty,StrokeThicknessProperty]);
    }

    public static readonly StyledProperty<IconKind> KindProperty =
        AvaloniaProperty.Register<EcliptixIcon, IconKind>(nameof(Kind));

    public IconKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<EcliptixIcon, IBrush?>(nameof(Stroke));

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<EcliptixIcon, double>(nameof(StrokeThickness), 2);

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}

