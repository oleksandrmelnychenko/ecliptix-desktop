using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using ReactiveUI;
using Splat;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;

namespace Ecliptix.Feature.Chats.ViewModels.Messages;

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
        ReplyCommand = ReactiveCommand.CreateFromTask(() => ExecuteAction(MessageActionType.Reply));
        CopyTextCommand = ReactiveCommand.CreateFromTask(() => ExecuteAction(MessageActionType.Copy));
        PinCommand = ReactiveCommand.CreateFromTask(() => ExecuteAction(MessageActionType.Pin));
        ForwardCommand = ReactiveCommand.CreateFromTask(() => ExecuteAction(MessageActionType.Forward));
        EditCommand = ReactiveCommand.CreateFromTask(() => ExecuteAction(MessageActionType.Edit));
        DeleteCommand = ReactiveCommand.CreateFromTask(() => ExecuteAction(MessageActionType.Delete));
    }

    private async Task ExecuteAction(MessageActionType actionType)
    {
        IMessageBus? messageBus = GetMessageBusInstance();

        if (messageBus != null)
        {
            await messageBus.PublishAsync(new ChatMessageActionEvent(actionType, this));
        }
    }

    private IMessageBus? GetMessageBusInstance()
    {
        return Locator.Current?.GetService<IMessageBus>();
    }

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
