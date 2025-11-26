using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Ecliptix.Core.Features.Chats.Models;
using Ecliptix.Core.Features.Chats.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Account;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;
using EUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public sealed class ChatsViewModel : ReactiveObject
{
    private readonly IChatService _chatService;

    private readonly Dictionary<Guid, object> _contentCache = new();

    public ChatSidebarViewModel SidebarViewModel { get; }

    [Reactive] public object? CurrentChatContent { get; set; }
    [Reactive] public bool IsTransitionReversed { get; set; }

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
