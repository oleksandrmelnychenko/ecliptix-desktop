using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace Ecliptix.Core.Controls.Core;

public partial class StatusBadge : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string>(nameof(Text));

    public static readonly StyledProperty<double> BadgeFontSizeProperty =
        AvaloniaProperty.Register<StatusBadge, double>(nameof(BadgeFontSize), 10.0);

    public static readonly StyledProperty<double> PopupFontSizeProperty =
        AvaloniaProperty.Register<StatusBadge, double>(nameof(PopupFontSize), 12.0);

    public static readonly StyledProperty<FontWeight> TooltipTitleFontWeightProperty =
        AvaloniaProperty.Register<StatusBadge, FontWeight>(nameof(TooltipTitleFontWeight), FontWeight.Medium);

    public static readonly StyledProperty<Geometry> IconProperty =
        AvaloniaProperty.Register<StatusBadge, Geometry>(nameof(Icon));

    public static readonly StyledProperty<IBrush> BadgeBrushProperty =
        AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(BadgeBrush), Brushes.Gray);

    public static readonly StyledProperty<IBrush> BadgeBackgroundProperty =
        AvaloniaProperty.Register<StatusBadge, IBrush>(nameof(BadgeBackground), Brushes.Transparent);

    public static readonly StyledProperty<string?> HoverTextProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(HoverText));

    public static readonly StyledProperty<string?> TooltipTitleProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(TooltipTitle));

    public static readonly StyledProperty<string?> TooltipFeature1Property =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(TooltipFeature1));

    public static readonly StyledProperty<string?> TooltipFeature2Property =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(TooltipFeature2));

    public static readonly StyledProperty<string?> TooltipFeature3Property =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(TooltipFeature3));

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? HoverText { get => GetValue(HoverTextProperty); set => SetValue(HoverTextProperty, value); }
    public string? TooltipTitle { get => GetValue(TooltipTitleProperty); set => SetValue(TooltipTitleProperty, value); }
    public string? TooltipFeature1 { get => GetValue(TooltipFeature1Property); set => SetValue(TooltipFeature1Property, value); }
    public string? TooltipFeature2 { get => GetValue(TooltipFeature2Property); set => SetValue(TooltipFeature2Property, value); }
    public string? TooltipFeature3 { get => GetValue(TooltipFeature3Property); set => SetValue(TooltipFeature3Property, value); }
    public Geometry Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IBrush BadgeBrush { get => GetValue(BadgeBrushProperty); set => SetValue(BadgeBrushProperty, value); }
    public IBrush BadgeBackground { get => GetValue(BadgeBackgroundProperty); set => SetValue(BadgeBackgroundProperty, value); }
    public double BadgeFontSize { get => GetValue(BadgeFontSizeProperty); set => SetValue(BadgeFontSizeProperty, value); }
    public double PopupFontSize { get => GetValue(PopupFontSizeProperty); set => SetValue(PopupFontSizeProperty, value); }
    public FontWeight TooltipTitleFontWeight { get => GetValue(TooltipTitleFontWeightProperty); set => SetValue(TooltipTitleFontWeightProperty, value); }

    private Popup? _infoPopup;
    private Border? _popupContentBorder;
    private Border? _containerBorder;

    private CancellationTokenSource? _closeCts;
    private readonly TimeSpan _animDuration = TimeSpan.FromMilliseconds(200);

    public StatusBadge()
    {
        InitializeComponent();
        _infoPopup = this.FindControl<Popup>("InfoPopup");
        _popupContentBorder = this.FindControl<Border>("PopupContentBorder");
        _containerBorder = this.FindControl<Border>("ContainerBorder");
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AdjustPopupPosition()
    {
        if (_infoPopup == null || _popupContentBorder == null || _containerBorder == null)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
        {
            return;
        }

        _popupContentBorder.Measure(Size.Infinity);
        Size popupSize = _popupContentBorder.DesiredSize;

        Point? targetPos = _containerBorder.TranslatePoint(new Point(0, 0), topLevel);
        if (targetPos == null)
        {
            return;
        }

        Rect targetRect = new(targetPos.Value, _containerBorder.Bounds.Size);
        Rect windowBounds = new(0, 0, topLevel.Bounds.Width, topLevel.Bounds.Height);

        double padding = 10.0;
        double spacing = 4.0;

        double finalHorizontalOffset = 0;
        double projectedRightEdge = targetRect.X + popupSize.Width;

        if (projectedRightEdge > windowBounds.Width - padding)
        {
            double overflow = projectedRightEdge - (windowBounds.Width - padding);
            finalHorizontalOffset = -overflow;
        }

        if (targetRect.X + finalHorizontalOffset < padding)
        {

             finalHorizontalOffset = padding - targetRect.X;
        }

        bool isInTopArea = targetRect.Y < (windowBounds.Height * 0.3);

        double finalVerticalOffset = spacing;
        double projectedBottomEdge = targetRect.Bottom + spacing + popupSize.Height;

        if (!isInTopArea && projectedBottomEdge > windowBounds.Height - padding)
        {

            finalVerticalOffset = -targetRect.Height - popupSize.Height - spacing;
        }

        _infoPopup.HorizontalOffset = finalHorizontalOffset;
        _infoPopup.VerticalOffset = finalVerticalOffset;
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);

        _closeCts?.Cancel();
        _closeCts = null;

        bool hasTooltip = !string.IsNullOrEmpty(HoverText) || !string.IsNullOrEmpty(TooltipTitle);

        if (_infoPopup != null && hasTooltip)
        {
            AdjustPopupPosition();

            _infoPopup.IsOpen = true;

            Dispatcher.UIThread.Post(() =>
            {
                _popupContentBorder?.Classes.Add("visible");
            }, DispatcherPriority.Render);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_infoPopup == null)
        {
            return;
        }

        _popupContentBorder?.Classes.Remove("visible");

        _closeCts = new CancellationTokenSource();
        CancellationToken token = _closeCts.Token;

        Task.Delay(_animDuration, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_closeCts != null && !_closeCts.IsCancellationRequested)
                    {
                        _infoPopup.IsOpen = false;
                    }
                });
            }
        });
    }
}
