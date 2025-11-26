using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Ecliptix.Core.Features.Chats.ViewModels;

namespace Ecliptix.Core.Features.Chats.Views;

public partial class ConversationView : UserControl
{
    private ScrollViewer? _chatScrollViewer;

    public ConversationView()
    {
        InitializeComponent();

        this.DataContextChanged += OnDataContextChanged;
        _chatScrollViewer = this.FindControl<ScrollViewer>("ChatScrollViewer");

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
}

