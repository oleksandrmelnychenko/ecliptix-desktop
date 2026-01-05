using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Ecliptix.Feature.Chats.Chats.Services;
using Ecliptix.Feature.Chats.Chats.ViewModels.Messages;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Splat;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;


namespace Ecliptix.Feature.Chats.Chats.ViewModels;

public enum MessageActionType
{
    Reply,
    Copy,
    Pin,
    Forward,
    Edit,
    Delete
}

public record ChatMessageActionEvent(MessageActionType ActionType, MessageViewModelBase Message);

public class ConversationViewModel : ReactiveObject, IDisposable
{
    private readonly IChatService _chatService = null!;
    private readonly Guid _chatId;
    private readonly IMessageBus? _messageBus;
    private readonly CompositeDisposable _disposables = new();
    public string Name { get; set; } = string.Empty;

    public Bitmap? Avatar { get; set; }

    public ObservableCollection<MessageViewModelBase> Messages { get; } = new();

    [Reactive] public bool IsInputContextVisible { get; set; }
    [Reactive] public string InputContextTitle { get; set; } = string.Empty;
    [Reactive] public string InputContextMessage { get; set; } = string.Empty;
    [Reactive] public IBrush InputContextColor { get; set; } = Brushes.Transparent;
    [Reactive] public string InputText { get; set; } = string.Empty;
    [Reactive] public Geometry SendIconData { get; set; } = Geometry.Parse("M2,3L2,10L17,12L2,14L2,21L23,12L2,3Z");

    private MessageViewModelBase? _targetMessage;
    private MessageActionType _currentAction = MessageActionType.Reply;

    private readonly Geometry _sendIcon = Geometry.Parse("M2,3L2,10L17,12L2,14L2,21L23,12L2,3Z");
    private readonly Geometry _checkIcon = Geometry.Parse("M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z");

    public ReactiveCommand<Unit, Unit> CancelInputContextCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SendMessageCommand { get; private set; } = null!;


    public ConversationViewModel(Guid chatId, string name, IChatService chatService)
    {
        _chatId = chatId;
        Name = name;
        _chatService = chatService;
        CancelInputContextCommand = ReactiveCommand.Create(ResetInputContext);
        SendMessageCommand = ReactiveCommand.CreateFromTask(OnSendAsync);

        _messageBus = Locator.Current?.GetService<IMessageBus>();

        if (_messageBus != null)
        {
            _messageBus.Subscribe<ChatMessageActionEvent>(OnMessageAction)
                .DisposeWith(_disposables);
        }

        LoadMessagesStreamAsync();
    }

    private async Task OnMessageAction(ChatMessageActionEvent evt)
    {
        switch (evt.ActionType)
        {
            case MessageActionType.Reply:
                ActivateReplyMode(evt.Message);
                break;
            case MessageActionType.Copy:
                break;
            case MessageActionType.Pin:
                break;
            case MessageActionType.Delete:
                break;
            case MessageActionType.Forward:
                break;
            case MessageActionType.Edit:
                ActivateEditMode(evt.Message);
                break;
        }
    }

    private void ActivateReplyMode(MessageViewModelBase msg)
    {
        IsInputContextVisible = true;
        InputContextTitle = $"Replying to {msg.SenderName}";
        InputContextMessage = msg.Text;
        InputContextColor = SolidColorBrush.Parse("#6A5ACD");
        SendIconData = _sendIcon;

    }

    private void ActivateEditMode(MessageViewModelBase msg)
    {
        IsInputContextVisible = true;
        InputContextTitle = "Editing message";
        InputContextMessage = msg.Text;
        InputContextColor = SolidColorBrush.Parse("#FFA500");

        InputText = msg.Text;
        SendIconData = _checkIcon;
    }

    private void ResetInputContext()
    {
        IsInputContextVisible = false;
        _targetMessage = null;
        InputText = string.Empty;
        SendIconData = _sendIcon;
    }


    private async Task OnSendAsync()
    {
        if (string.IsNullOrWhiteSpace(InputText))
        {
            return;
        }

        if (IsInputContextVisible && _currentAction == MessageActionType.Edit && _targetMessage != null)
        {
            // await _chatService.EditMessageAsync(_chatId, _targetMessage.Id, InputText);
        }
        else if (IsInputContextVisible && _currentAction == MessageActionType.Reply && _targetMessage != null)
        {
            // await _chatService.ReplyToMessageAsync(_chatId, _targetMessage.Id, InputText);
        }
        else
        {
            // await _chatService.SendMessageAsync(_chatId, InputText);
        }

        ResetInputContext();
    }

    public ConversationViewModel() { }

    private async void LoadMessagesStreamAsync()
    {
        Messages.Clear();

        await foreach (IEnumerable<MessageViewModelBase> batch in _chatService.GetMessagesStreamAsync(_chatId))
        {
            IOrderedEnumerable<MessageViewModelBase> sortedBatch = batch.OrderBy(x => x.Time);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (MessageViewModelBase msg in sortedBatch)
                {
                    Messages.Add(msg);
                }
            });
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
