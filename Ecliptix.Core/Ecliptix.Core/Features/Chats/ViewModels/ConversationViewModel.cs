using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using Ecliptix.Core.Features.Chats.Services;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;
using ReactiveUI;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class ConversationViewModel : ReactiveObject
{
    private readonly IChatService _chatService;
    private readonly Guid _chatId;

    public string Name { get; set; }

    public ObservableCollection<MessageViewModelBase> Messages { get; } = new();

    public ConversationViewModel(Guid chatId, string name, IChatService chatService)
    {
        _chatId = chatId;
        Name = name;
        _chatService = chatService;

        LoadMessagesStreamAsync();
    }

    public ConversationViewModel() { }

    private async void LoadMessagesStreamAsync()
    {
        Messages.Clear();

        await foreach (IEnumerable<MessageViewModelBase> batch in _chatService.GetMessagesStreamAsync(_chatId))
        {
            IOrderedEnumerable<MessageViewModelBase> sortedBatch = batch.OrderByDescending(x => x.Time);

            foreach (MessageViewModelBase msg in sortedBatch)
            {
                Messages.Insert(0, msg);
            }
        }
    }
}
