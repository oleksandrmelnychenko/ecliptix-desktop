using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Ecliptix.Core.Features.Chats.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public class ChatSidebarViewModel : ReactiveObject
{
    private readonly IChatSidebarService _chatService;

    public ObservableCollection<ChatListItemViewModel> Chats { get; } = new();

    [Reactive] public ChatListItemViewModel? SelectedChat { get; set; }

    public ChatSidebarViewModel(IChatSidebarService chatService)
    {
        _chatService = chatService;
        LoadChatsAsync().ConfigureAwait(false);
    }

    private async Task LoadChatsAsync()
    {
        IEnumerable<ChatListItemViewModel> chats = await _chatService.GetChatsAsync();

        foreach (ChatListItemViewModel chat in chats)
        {
            Chats.Add(chat);
        }

        if (Chats.Count > 0)
        {
            SelectedChat = Chats[0];
        }
    }
}
