using System;
using Avalonia.Media.Imaging;
using ReactiveUI;

namespace Ecliptix.Core.Features.Chats.ViewModels.Messages;

public abstract class MessageViewModelBase : ReactiveObject
{
    public string Text { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public bool IsMine { get; set; }
    public string SenderName { get; set; } = string.Empty;

    public Bitmap? SenderAvatar { get; set; }

    public string TimeDisplay => Time.ToString("t");
}

public class SimpleMessageViewModel : MessageViewModelBase
{
}

public class ReplyMessageViewModel : MessageViewModelBase
{
    public string QuotedText { get; set; } = string.Empty;
    public string QuotedAuthor { get; set; } = string.Empty;
}
