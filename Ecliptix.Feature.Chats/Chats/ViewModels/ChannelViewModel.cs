using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Utilities;
using Ecliptix.Feature.Chats.Chats.Services;
using Ecliptix.Feature.Chats.Chats.ViewModels.Messages;
using ReactiveUI;
using Serilog;

namespace Ecliptix.Feature.Chats.Chats.ViewModels;

public class ChannelViewModel : ReactiveObject
{
    private readonly IChatService _chatService = null!;
    private readonly Guid _chatId;

    public string Name { get; set; } = string.Empty;

    public ObservableCollection<MessageViewModelBase> Posts { get; } = new();

    public ChannelViewModel(Guid chatId, string name, IChatService chatService)
    {
        _chatId = chatId;
        Name = name;
        _chatService = chatService;
        LoadPostsAsync().DoSafeAsync(ex => Log.Error(ex, "Error load posts."));
    }

    public ChannelViewModel() { } // Design-time

    private async Task LoadPostsAsync()
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
