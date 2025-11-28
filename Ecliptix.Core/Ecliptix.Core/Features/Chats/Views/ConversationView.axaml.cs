using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ecliptix.Core.Features.Chats.ViewModels;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;

namespace Ecliptix.Core.Features.Chats.Views;

public partial class ConversationView : UserControl
{
    private ScrollViewer? _chatScrollViewer;
    private readonly Flyout _messageActionFlyout;
    private bool _isClosingAnimationRunning;


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
        _messageActionFlyout.Hide();
        _messageActionFlyout.Closing -= OnFlyoutClosing;
        base.OnDetachedFromVisualTree(e);
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ConversationViewModel vm)
        {
            vm.Messages.CollectionChanged -= OnMessagesChanged;

            vm.Messages.CollectionChanged += OnMessagesChanged;
        }
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

        MessageActionMenu menuContent = new MessageActionMenu { DataContext = messageViewModel };
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

