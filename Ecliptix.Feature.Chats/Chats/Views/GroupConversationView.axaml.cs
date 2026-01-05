using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Ecliptix.Feature.Chats.Chats.ViewModels;

namespace Ecliptix.Feature.Chats.Chats.Views;

public partial class GroupConversationView : UserControl
{
    private ScrollViewer? _chatScrollViewer;

    public GroupConversationView()
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
        if (DataContext is GroupConversationViewModel vm)
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

