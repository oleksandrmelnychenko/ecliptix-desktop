using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ecliptix.Feature.Chats.Chats.ViewModels;
using Ecliptix.Feature.Chats.Chats.ViewModels.Messages;
using ReactiveUI;

namespace Ecliptix.Feature.Chats.Chats.Views;

public partial class ConversationView : UserControl
{
    private ScrollViewer? _chatScrollViewer;
    private readonly Flyout _messageActionFlyout;
    private bool _isClosingAnimationRunning;

    private DispatcherTimer? _scrollTimer;

    private const double AUTO_SCROLL_THRESHOLD = 50.0;

    public ConversationView()
    {
        InitializeComponent();

        this.DataContextChanged += OnDataContextChanged;
        _chatScrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");

        _messageActionFlyout = new Flyout
        {
            FlyoutPresenterClasses = { "transparent" },
            Placement = PlacementMode.Pointer,
            ShowMode = FlyoutShowMode.Standard
        };

        _messageActionFlyout.Closing += OnFlyoutClosing;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _scrollTimer?.Stop();
        _messageActionFlyout.Hide();
        _messageActionFlyout.Closing -= OnFlyoutClosing;
        base.OnDetachedFromVisualTree(e);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnFlyoutClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosingAnimationRunning)
        {
            return;
        }

        e.Cancel = true;
        _isClosingAnimationRunning = true;

        if (_messageActionFlyout.Content is MessageActionMenu menu)
        {
            await menu.AnimateCloseAsync();
        }

        _messageActionFlyout.Hide();
        _isClosingAnimationRunning = false;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ConversationViewModel vm)
        {
            vm.Messages.CollectionChanged -= OnMessagesChanged;
            vm.Messages.CollectionChanged += OnMessagesChanged;

            vm.WhenAnyValue(x => x.IsInputContextVisible)
                .Subscribe(isVisible =>
                {
                    if (isVisible)
                    {
                        _messageActionFlyout.Hide();

                        FocusInput();

                        AnimateScrollOnOpen();
                    }
                });
        }
    }

    private void AnimateScrollOnOpen()
    {
        if (_chatScrollViewer == null)
        {
            return;
        }

        UserControl? chatInputControl = this.FindControl<UserControl>("ChatInput");
        if (chatInputControl == null)
        {
            return;
        }

        double totalDurationMs = 250.0;
        if (chatInputControl.TryGetResource("PanelAnimDuration", out object? durationRes)
            && durationRes is TimeSpan durationSpan)
        {
            totalDurationMs = durationSpan.TotalMilliseconds;
        }

        Border? contextPanel = chatInputControl.FindControl<Border>("ContextPanel");
        double targetPanelHeight = 0;

        if (contextPanel != null)
        {
            contextPanel.Measure(Size.Infinity);
            targetPanelHeight = contextPanel.DesiredSize.Height;

            if (targetPanelHeight == 0)
            {
                if (chatInputControl.TryGetResource("ReplyHeight", out object? heightRes) && heightRes is double heightVal)
                {
                    targetPanelHeight = heightVal;
                }
                else
                {
                    targetPanelHeight = 46.0;
                }
            }
        }

        double maxOffset = _chatScrollViewer.Extent.Height - _chatScrollViewer.Viewport.Height;
        bool isAtBottom = (maxOffset - _chatScrollViewer.Offset.Y) <= AUTO_SCROLL_THRESHOLD;

        if (!isAtBottom)
        {
            return;
        }

        _scrollTimer?.Stop();

        double startOffset = _chatScrollViewer.Offset.Y;
        double targetOffset = startOffset + targetPanelHeight;

        DateTime startTime = DateTime.UtcNow;

        _scrollTimer = new DispatcherTimer(
            TimeSpan.Zero,
            DispatcherPriority.Render,
            (sender, e) =>
            {
                DateTime now = DateTime.UtcNow;
                double elapsedMs = (now - startTime).TotalMilliseconds;

                double progress = Math.Min(1.0, elapsedMs / totalDurationMs);

                double t = 1.0 - progress;
                double easedProgress = 1.0 - (t * t * t);

                double currentOffset = startOffset + (targetOffset - startOffset) * easedProgress;

                _chatScrollViewer.Offset = new Vector(_chatScrollViewer.Offset.X, currentOffset);

                if (progress >= 1.0)
                {
                    (sender as DispatcherTimer)?.Stop();
                }
            });

        _scrollTimer.Start();
    }

    private void FocusInput()
    {
        Control? chatInputView = this.FindControl<Control>("ChatInput");
        TextBox? textBox = chatInputView?.FindDescendantOfType<TextBox>();
        textBox?.Focus();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Reset)
        {
            ScrollToBottom();
        }
    }

    private void ScrollToBottom()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _chatScrollViewer?.ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void OnMessagePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint currentPoint = e.GetCurrentPoint(this);
        if (!currentPoint.Properties.IsRightButtonPressed)
        {
            return;
        }

        if (sender is not Control control)
        {
            return;
        }

        if (control.DataContext is not MessageViewModelBase messageViewModel)
        {
            return;
        }

        Canvas? measureContainer = this.FindControl<Canvas>("HiddenMeasureContainer");
        if (measureContainer == null)
        {
            return;
        }

        MessageActionMenu menuContent = new() { DataContext = messageViewModel };

        measureContainer.Children.Add(menuContent);
        menuContent.Measure(Size.Infinity);
        double menuWidth = menuContent.DesiredSize.Width;
        double menuHeight = menuContent.DesiredSize.Height;
        measureContainer.Children.Remove(menuContent);

        _messageActionFlyout.Content = menuContent;

        Window? window = this.FindAncestorOfType<Window>();
        if (window == null)
        {
            return;
        }

        Point pointerPos = e.GetPosition(window);
        Rect windowBounds = window.Bounds;

        double edgePadding = 10.0;
        double offsetX = 0;
        double offsetY = 0;

        double predictedRightEdge = pointerPos.X + menuWidth + edgePadding;
        if (predictedRightEdge > windowBounds.Width)
        {
            double overflowX = predictedRightEdge - windowBounds.Width;
            offsetX = -overflowX;
        }

        double predictedBottomEdge = pointerPos.Y + menuHeight + edgePadding;
        if (predictedBottomEdge > windowBounds.Height)
        {
            double overflowY = predictedBottomEdge - windowBounds.Height;
            offsetY = -overflowY;
        }

        _messageActionFlyout.HorizontalOffset = offsetX;
        _messageActionFlyout.VerticalOffset = offsetY;

        _messageActionFlyout.ShowAt(control);
        e.Handled = true;
    }
}

