using System;
using System.Collections.Generic;
using Ecliptix.Core.Features.Chats.Models;
using Ecliptix.Core.Features.Chats.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public sealed class ChatsViewModel : ReactiveObject
{
    private readonly IChatService _chatService;

    private readonly Dictionary<Guid, object> _contentCache = new();

    public ChatSidebarViewModel SidebarViewModel { get; }

    [Reactive] public object? CurrentChatContent { get; set; }
    [Reactive] public bool IsTransitionReversed { get; set; }

    private readonly SearchChatViewModel _searchViewModel = new();

    public ChatsViewModel()
    {
        _chatService = new ChatService();

        SidebarViewModel = new ChatSidebarViewModel(_chatService);

        this.WhenAnyValue(x => x.SidebarViewModel.SelectedChat)
            .Subscribe(chat =>
            {
                if (chat != null)
                {
                    CurrentChatContent = GetOrCreateChatViewModel(chat);
                }
            });

        SidebarViewModel.OpenSearchCommand
            .Subscribe(_ =>
            {
                CurrentChatContent = _searchViewModel;
            });
    }

    private object GetOrCreateChatViewModel(ChatListItemViewModel chat)
    {
        if (_contentCache.TryGetValue(chat.Id, out object? cachedViewModel))
        {
            return cachedViewModel;
        }

        object newViewModel = chat.Type switch
        {
            ChatType.Personal => new ConversationViewModel(chat.Id, chat.Title, _chatService),
            ChatType.Group => new GroupConversationViewModel(chat.Id, chat.Title, _chatService),
            ChatType.Channel => new ChannelViewModel(chat.Id, chat.Title, _chatService),

            _ => new ConversationViewModel(chat.Id, chat.Title, _chatService)
        };

        _contentCache[chat.Id] = newViewModel;

        return newViewModel;
    }

    public void ClearCache()
    {
        _contentCache.Clear();
    }
}
