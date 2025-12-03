using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
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

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? HoverText
    {
        get => GetValue(HoverTextProperty);
        set => SetValue(HoverTextProperty, value);
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
    private TextBlock? _measuringBlock;
    private CancellationTokenSource? _animCts;
    private string? _originalText;

    private const string DurationResourceKey = "BadgeAnimationDuration";
    private const string ColorDurationResourceKey = "BadgeColorDuration";
    private const string PaddingResourceKey = "BadgePadding";
    private const string BorderThicknessResourceKey = "BadgeBorderThickness";
    private const string IconSizeResourceKey = "BadgeIconSize";
    private const string IconSpacingResourceKey = "BadgeIconSpacing";

    public StatusBadge()
    {
        InitializeComponent();
        _containerBorder = this.FindControl<Border>("ContainerBorder");
        _measuringBlock = this.FindControl<TextBlock>("MeasuringBlock");
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
            AnimateToNewText(HoverText);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_originalText != null)
        {
            AnimateToNewText(_originalText);
            _originalText = null;
        }
    }

    private async void AnimateToNewText(string newText)
    {
        if (_containerBorder == null || _measuringBlock == null)
        {
            return;
        }

        _animCts?.Cancel();
        _animCts = new CancellationTokenSource();
        CancellationToken token = _animCts.Token;

        try
        {
            double currentVisualWidth = _containerBorder.Bounds.Width;

            if (currentVisualWidth <= 0 || double.IsNaN(currentVisualWidth))
            {
                Text = newText;
                return;
            }

            _containerBorder.Transitions = null;
            _containerBorder.Width = currentVisualWidth;

            await Task.Delay(15, token);

            double targetWidth = CalculateRequiredWidth(newText);
            TimeSpan duration = GetResourceValue(DurationResourceKey, TimeSpan.FromSeconds(0.3));

            UpdateTransitions(duration);

            Text = newText;
            _containerBorder.Width = targetWidth;

            int delayMs = (int)duration.TotalMilliseconds + 50;
            await Task.Delay(delayMs, token);

            if (!token.IsCancellationRequested)
            {
                _containerBorder.Transitions = null;
                _containerBorder.Width = double.NaN;

                await Task.Delay(20, token);
                UpdateTransitions(duration);
            }
        }
        catch (TaskCanceledException)
        {
        }
    }

    private void UpdateTransitions(TimeSpan duration)
    {
        if (_containerBorder == null)
        {
            return;
        }

        if (_containerBorder.Transitions != null && _containerBorder.Transitions.Count > 0)
        {
            return;
        }

        TimeSpan colorDuration = GetResourceValue(ColorDurationResourceKey, TimeSpan.FromSeconds(0.2));

        _containerBorder.Transitions = new Transitions
        {
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = duration, Easing = new CubicEaseOut() },
            new BrushTransition { Property = TemplatedControl.BackgroundProperty, Duration = colorDuration },
            new BrushTransition { Property = TemplatedControl.BorderBrushProperty, Duration = colorDuration }
        };
    }

    private double CalculateRequiredWidth(string text)
    {
        if (_measuringBlock == null)
        {
            return 0;
        }

        _measuringBlock.Text = text;
        _measuringBlock.Measure(Size.Infinity);

        double textWidth = _measuringBlock.DesiredSize.Width;

        Thickness padding = GetResourceValue(PaddingResourceKey, new Thickness(12, 0));
        Thickness border = GetResourceValue(BorderThicknessResourceKey, new Thickness(1));
        double iconSize = GetResourceValue(IconSizeResourceKey, 16.0);
        double iconSpacing = GetResourceValue(IconSpacingResourceKey, 8.0);

        double totalWidth = textWidth + padding.Left + padding.Right + border.Left + border.Right;

        if (Icon != null)
        {
            totalWidth += iconSize + iconSpacing;
        }

        return totalWidth;
    }

    private T GetResourceValue<T>(string key, T defaultValue)
    {
        if (this.TryGetResource(key, null, out object? res) && res is T typedRes)
        {
            return typedRes;
        }
        return defaultValue;
    }
}

