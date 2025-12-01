using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ecliptix.Core.Features.Chats.Services;
using Ecliptix.Core.Features.Chats.ViewModels.Messages;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class GroupConversationViewModel : ReactiveObject
{
    private readonly IChatService _chatService;
    private readonly Guid _chatId;

    public string Name { get; set; }

    public ObservableCollection<Bitmap> ParticipantsAvatars { get; } = new();

    [Reactive] public string MembersCountText { get; set; } = "";

    public ObservableCollection<MessageViewModelBase> Messages { get; } = new();

    public GroupConversationViewModel(Guid chatId, string name, IChatService chatService)
    {
        _chatId = chatId;
        Name = name;
        _chatService = chatService;

        LoadHeaderData();
        LoadMessagesStreamAsync();
    }

    public GroupConversationViewModel() { }

    private async void LoadHeaderData()
    {

        List<Bitmap> avatars = await _chatService.GetChatParticipantsAvatarsAsync(_chatId, 4);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ParticipantsAvatars.Clear();
            foreach (Bitmap av in avatars)
            {
                ParticipantsAvatars.Add(av);
            }

            int count = _chatService.GetChatParticipantsCount(_chatId);
            MembersCountText = $"{count} members";
        });
    }

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
