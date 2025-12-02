using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls.Core;

public partial class StatusBadge : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text));

    public static readonly StyledProperty<Geometry> IconProperty =
        AvaloniaProperty.Register<StatusBadge, Geometry>(nameof(Icon));

    public static readonly StyledProperty<IBrush> BadgeBrushProperty =
        AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(BadgeBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> BadgeBackgroundProperty =
        AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(BadgeBackground), Brushes.Transparent);

    public static readonly StyledProperty<string?> HoverTextProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(HoverText));

    public string? HoverText
    {
        get => GetValue(HoverTextProperty);
        set => SetValue(HoverTextProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public Geometry Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public IBrush BadgeBrush
    {
        get => GetValue(BadgeBrushProperty);
        set => SetValue(BadgeBrushProperty, value);
    }

    public IBrush BadgeBackground
    {
        get => GetValue(BadgeBackgroundProperty);
        set => SetValue(BadgeBackgroundProperty, value);
    }

    private Border? _containerBorder;
    private CancellationTokenSource? _animationCts;
    private string? _originalText;

    public StatusBadge()
    {
        InitializeComponent();
        _containerBorder = this.FindControl<Border>("ContainerBorder");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        if (!string.IsNullOrEmpty(HoverText) && HoverText != Text)
        {
            _originalText = Text;
            Text = HoverText;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_originalText != null)
        {
            Text = _originalText;
            _originalText = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty || change.Property == IconProperty)
        {
            if (_containerBorder != null && _containerBorder.IsLoaded && _containerBorder.Bounds.Width > 0)
            {
                _containerBorder.Width = _containerBorder.Bounds.Width;
                Dispatcher.UIThread.Post(AnimateSizeChange, DispatcherPriority.Background);
            }
        }
    }

    private async void AnimateSizeChange()
    {
        if (_containerBorder == null)
        {
            return;
        }

        _animationCts?.Cancel();
        _animationCts = new CancellationTokenSource();
        CancellationToken token = _animationCts.Token;

        double oldWidth = _containerBorder.Width;

        _containerBorder.InvalidateMeasure();

        _containerBorder.Measure(Size.Infinity);
        double targetWidth = _containerBorder.DesiredSize.Width;

        if (Math.Abs(oldWidth - targetWidth) < 1)
        {
            _containerBorder.Width = double.NaN;
            return;
        }

        Animation animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(300),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(WidthProperty, oldWidth) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(WidthProperty, targetWidth) } }
            }
        };

        try
        {
            await animation.RunAsync(_containerBorder, token);
        }
        catch (TaskCanceledException) { }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _containerBorder.Width = double.NaN;
            }
        }
    }
}

