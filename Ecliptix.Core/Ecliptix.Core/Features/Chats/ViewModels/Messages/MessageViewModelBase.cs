using System;
using System.Reactive;
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

    public ReactiveCommand<Unit, Unit> ReplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyTextCommand { get; }
    public ReactiveCommand<Unit, Unit> PinCommand { get; }
    public ReactiveCommand<Unit, Unit> ForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> EditCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

    protected MessageViewModelBase()
    {
        ReplyCommand = ReactiveCommand.Create(OnReply);
        CopyTextCommand = ReactiveCommand.Create(OnCopyText);
        PinCommand = ReactiveCommand.Create(OnPin);
        ForwardCommand = ReactiveCommand.Create(OnForward);
        EditCommand = ReactiveCommand.Create(OnEdit);
        DeleteCommand = ReactiveCommand.Create(OnDelete);
    }

    private void OnReply() { /* TODO: Implement Reply logic */ }
    private void OnCopyText() { /* TODO: Implement Copy logic */ }
    private void OnPin() { /* TODO: Implement Pin logic */ }
    private void OnForward() { /* TODO: Implement Forward logic */ }
    private void OnEdit() { /* TODO: Implement Edit logic */ }
    private void OnDelete() { /* TODO: Implement Delete logic */ }
}

public class SimpleMessageViewModel : MessageViewModelBase
{
}

public class ReplyMessageViewModel : MessageViewModelBase
{
    public string QuotedText { get; set; } = string.Empty;
    public string QuotedAuthor { get; set; } = string.Empty;
}

public class DateSeparatorViewModel : MessageViewModelBase
{
    public string DateDisplay => Time.ToString("MMMM dd, yyyy");
}
