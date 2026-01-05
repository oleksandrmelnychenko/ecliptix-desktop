using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Feature.Chats.Chats.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Chats.Chats.ViewModels;

public class ChatSidebarViewModel : ReactiveObject
{
    private readonly IChatService _chatService;

    public ObservableCollection<ChatListItemViewModel> Chats { get; } = new();

    [Reactive] public ChatListItemViewModel? SelectedChat { get; set; }

    public ReactiveCommand<Unit, Unit> OpenSearchCommand { get; }


    public ChatSidebarViewModel(IChatService chatService)
    {
        _chatService = chatService;
        LoadChatsAsync().ConfigureAwait(false);

        OpenSearchCommand = ReactiveCommand.Create(() =>
        {
            SelectedChat = null;
        });
    }

    private async Task LoadChatsAsync()
    {
        IEnumerable<ChatListItemViewModel> chats = await _chatService.GetChatsAsync();

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (ChatListItemViewModel chat in chats)
            {
                Chats.Add(chat);
            }

            if (Chats.Count > 0)
            {
                SelectedChat = Chats[0];
            }
        });
    }
}
