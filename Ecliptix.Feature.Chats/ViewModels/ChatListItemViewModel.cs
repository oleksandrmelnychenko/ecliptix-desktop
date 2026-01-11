using System;
using System.Linq;
using System.Reactive.Linq;
using Avalonia.Media.Imaging;
using Ecliptix.Feature.Chats.Domain.Models;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Chats.ViewModels;

public sealed class ChatListItemViewModel : ReactiveObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Reactive] public string Title { get; set; } = string.Empty;
    [Reactive] public ChatType Type { get; set; }
    [Reactive] public bool IsOnline { get; set; }
    [Reactive] public bool IsPinned { get; set; }

    [Reactive] public int UnreadCount { get; set; }

    [Reactive] public string LastMessageRaw { get; set; } = string.Empty;
    [Reactive] public string LastMessageSender { get; set; } = string.Empty;
    [Reactive] public DateTime LastMessageTime { get; set; }
    [Reactive] public Bitmap? AvatarImage { get; set; }

    private readonly ObservableAsPropertyHelper<bool> _hasUnreadMessages;

    public bool HasUnreadMessages => _hasUnreadMessages.Value;

    public string Initials => string.IsNullOrEmpty(Title) ? "?" : string.Concat(Title.Split(' ').Take(2).Select(s => s[0])).ToUpper();

    public string DisplayMessage
    {
        get
        {
            if (Type == ChatType.Group || Type == ChatType.Channel)
            {
                return $"{LastMessageSender}: {LastMessageRaw}";
            }

            return LastMessageRaw;
        }
    }

    public string DisplayTime => LastMessageTime.Date == DateTime.Today
        ? LastMessageTime.ToString("HH:mm")
        : LastMessageTime.ToString("MMM dd");

    public ChatListItemViewModel()
    {
        _hasUnreadMessages = this.WhenAnyValue(x => x.UnreadCount)
            .Select(count => count > 0)
            .ToProperty(this, x => x.HasUnreadMessages);
    }

    public object CreateContentViewModel()
    {
        return Type switch
        {
            ChatType.Personal => new ConversationViewModel { Name = Title },
            ChatType.Group => new GroupConversationViewModel { Name = Title },
            ChatType.Channel => new ChannelViewModel { Name = Title },
            _ => new ConversationViewModel { Name = Title }
        };
    }
}
