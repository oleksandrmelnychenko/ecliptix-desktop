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
using Avalonia.VisualTree;

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

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? HoverText { get => GetValue(HoverTextProperty); set => SetValue(HoverTextProperty, value); }
    public Geometry Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IBrush BadgeBrush { get => GetValue(BadgeBrushProperty); set => SetValue(BadgeBrushProperty, value); }
    public IBrush BadgeBackground { get => GetValue(BadgeBackgroundProperty); set => SetValue(BadgeBackgroundProperty, value); }

    private Popup? _infoPopup;
    private Border? _popupContentBorder;
    private Border? _containerBorder;

    private CancellationTokenSource? _closeCts;
    private readonly TimeSpan _animDuration = TimeSpan.FromMilliseconds(150);

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

        Rect targetRect = new Rect(targetPos.Value, _containerBorder.Bounds.Size);
        Rect windowBounds = new Rect(0, 0, topLevel.Bounds.Width, topLevel.Bounds.Height);

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

        double finalVerticalOffset = spacing;
        double projectedBottomEdge = targetRect.Bottom + spacing + popupSize.Height;

        if (projectedBottomEdge > windowBounds.Height - padding)
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

        if (_infoPopup != null && !string.IsNullOrEmpty(HoverText))
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
