using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Ecliptix.Core.Features.Chats.Services;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;
using ReactiveUI;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class GroupConversationViewModel : ReactiveObject
{
    private readonly IChatService _chatService;
    private readonly Guid _chatId;

    public string Name { get; set; }

    // Тут можна додати властивість ParticipantsCount або AvatarList для хедера

    public ObservableCollection<MessageViewModelBase> Messages { get; } = new();

    public GroupConversationViewModel(Guid chatId, string name, IChatService chatService)
    {
        _chatId = chatId;
        Name = name;
        _chatService = chatService;

        LoadMessagesStreamAsync();
    }

    // Конструктор без параметрів для XAML прев'ю
    public GroupConversationViewModel() { }

    private async void LoadMessagesStreamAsync()
    {
        Messages.Clear();
        await foreach (IEnumerable<MessageViewModelBase> batch in _chatService.GetMessagesStreamAsync(_chatId))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (MessageViewModelBase msg in batch)
                {
                    Messages.Add(msg);
                }
            });
        }
    }
}
