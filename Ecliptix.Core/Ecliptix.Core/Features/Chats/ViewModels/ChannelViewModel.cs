using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Ecliptix.Core.Features.Chats.Services;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;
using ReactiveUI;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class ChannelViewModel : ReactiveObject
{
    private readonly IChatService _chatService;
    private readonly Guid _chatId;

    public string Name { get; set; }

    public ObservableCollection<MessageViewModelBase> Posts { get; } = new();

    public ChannelViewModel(Guid chatId, string name, IChatService chatService)
    {
        _chatId = chatId;
        Name = name;
        _chatService = chatService;
        LoadPosts();
    }

    public ChannelViewModel() { } // Design-time

    private async void LoadPosts()
    {
        Posts.Clear();
        await foreach (IEnumerable<MessageViewModelBase> batch in _chatService.GetMessagesStreamAsync(_chatId))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (MessageViewModelBase item in batch)
                {
                    Posts.Add(item);
                }
            });
        }
    }
}
