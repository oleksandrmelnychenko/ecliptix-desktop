using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Ecliptix.Core.Features.Chats.Models;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public sealed class ChatWindowViewModel : Ecliptix.Core.Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private readonly Dictionary<string, List<ChatMessage>> _conversationMessages = new();
    private bool _isDisposed;

    [Reactive] public ChatConversation? SelectedConversation { get; set; }
    [Reactive] public string MessageText { get; set; } = string.Empty;
    [Reactive] public bool IsTyping { get; set; }
    [Reactive] public bool IsRecording { get; set; }

    public ObservableCollection<ChatMessage> Messages { get; }

    public ReactiveCommand<Unit, Unit> SendMessageCommand { get; }
    public ReactiveCommand<Unit, Unit> AttachFileCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCallCommand { get; }
    public ReactiveCommand<Unit, Unit> StartVideoCallCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowInfoCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleEmojiCommand { get; }
    public ReactiveCommand<Unit, Unit> StartVoiceRecordingCommand { get; }

    public ChatWindowViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        Messages = new ObservableCollection<ChatMessage>();

        GenerateMockMessages();

        SendMessageCommand = ReactiveCommand.Create(() =>
        {
            if (string.IsNullOrWhiteSpace(MessageText) || SelectedConversation == null)
            {
                return;
            }

            ChatMessage newMessage = new()
            {
                Id = Guid.NewGuid().ToString(),
                ConversationId = SelectedConversation.Id,
                SenderId = "me",
                SenderName = "You",
                Content = MessageText,
                Timestamp = DateTime.Now,
                IsSent = true,
                IsRead = false,
                Status = MessageStatus.Sent,
                Type = MessageType.Text
            };

            Messages.Add(newMessage);
            MessageText = string.Empty;
        });

        AttachFileCommand = ReactiveCommand.Create(() =>
        {
        });

        StartCallCommand = ReactiveCommand.Create(() =>
        {
        });

        StartVideoCallCommand = ReactiveCommand.Create(() =>
        {
        });

        ShowInfoCommand = ReactiveCommand.Create(() =>
        {
        });

        ToggleEmojiCommand = ReactiveCommand.Create(() =>
        {
        });

        StartVoiceRecordingCommand = ReactiveCommand.Create(() =>
        {
            IsRecording = !IsRecording;
        });

        this.WhenAnyValue(x => x.SelectedConversation)
            .WhereNotNull()
            .Subscribe(conversation =>
            {
                Messages.Clear();
                if (_conversationMessages.TryGetValue(conversation.Id, out List<ChatMessage>? messages))
                {
                    foreach (ChatMessage message in messages)
                    {
                        Messages.Add(message);
                    }
                }
            })
            .DisposeWith(_disposables);
    }

    private void GenerateMockMessages()
    {
        _conversationMessages["1"] = GenerateMessagesForAlice();
        _conversationMessages["2"] = GenerateMessagesForBob();
        _conversationMessages["3"] = GenerateMessagesForTeamDesign();
        _conversationMessages["4"] = GenerateMessagesForCharlie();
        _conversationMessages["5"] = GenerateMessagesForDiana();
        _conversationMessages["6"] = GenerateMessagesForEngineeringTeam();
        _conversationMessages["7"] = GenerateMessagesForFrank();
        _conversationMessages["8"] = GenerateMessagesForGrace();
    }

    private List<ChatMessage> GenerateMessagesForAlice() =>
    [
        new ChatMessage
        {
            Id = "1-1",
            ConversationId = "1",
            SenderId = "alice",
            SenderName = "Alice Johnson",
            Content = "Hey! How's your day going?",
            Timestamp = DateTime.Now.AddHours(-2),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "1-2",
            ConversationId = "1",
            SenderId = "me",
            SenderName = "You",
            Content = "Pretty good! Just working on some new features",
            Timestamp = DateTime.Now.AddHours(-2).AddMinutes(5),
            IsSent = true,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "1-3",
            ConversationId = "1",
            SenderId = "alice",
            SenderName = "Alice Johnson",
            Content = "That sounds exciting! What are you working on?",
            Timestamp = DateTime.Now.AddHours(-2).AddMinutes(7),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "1-4",
            ConversationId = "1",
            SenderId = "me",
            SenderName = "You",
            Content = "Building a chat interface actually 😄",
            Timestamp = DateTime.Now.AddHours(-2).AddMinutes(10),
            IsSent = true,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "1-5",
            ConversationId = "1",
            SenderId = "alice",
            SenderName = "Alice Johnson",
            Content = "Hey, how are you?",
            Timestamp = DateTime.Now.AddMinutes(-2),
            IsSent = false,
            IsRead = false,
            Status = MessageStatus.Delivered,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForBob() =>
    [
        new ChatMessage
        {
            Id = "2-1",
            ConversationId = "2",
            SenderId = "bob",
            SenderName = "Bob Smith",
            Content = "Don't forget about the meeting",
            Timestamp = DateTime.Now.AddHours(-2),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "2-2",
            ConversationId = "2",
            SenderId = "me",
            SenderName = "You",
            Content = "Thanks for the reminder!",
            Timestamp = DateTime.Now.AddHours(-2).AddMinutes(5),
            IsSent = true,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "2-3",
            ConversationId = "2",
            SenderId = "bob",
            SenderName = "Bob Smith",
            Content = "Meeting at 3pm",
            Timestamp = DateTime.Now.AddHours(-1),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForTeamDesign() =>
    [
        new ChatMessage
        {
            Id = "3-1",
            ConversationId = "3",
            SenderId = "designer1",
            SenderName = "Sarah Designer",
            Content = "Hey team, I've uploaded the new mockups",
            Timestamp = DateTime.Now.AddHours(-5),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "3-2",
            ConversationId = "3",
            SenderId = "me",
            SenderName = "You",
            Content = "Great! I'll take a look",
            Timestamp = DateTime.Now.AddHours(-4),
            IsSent = true,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "3-3",
            ConversationId = "3",
            SenderId = "designer2",
            SenderName = "John Designer",
            Content = "New mockups ready",
            Timestamp = DateTime.Now.AddHours(-3),
            IsSent = false,
            IsRead = false,
            Status = MessageStatus.Delivered,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForCharlie() =>
    [
        new ChatMessage
        {
            Id = "4-1",
            ConversationId = "4",
            SenderId = "me",
            SenderName = "You",
            Content = "Here's that document you asked for",
            Timestamp = DateTime.Now.AddDays(-1).AddHours(-2),
            IsSent = true,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "4-2",
            ConversationId = "4",
            SenderId = "charlie",
            SenderName = "Charlie Brown",
            Content = "Thanks!",
            Timestamp = DateTime.Now.AddDays(-1),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForDiana() =>
    [
        new ChatMessage
        {
            Id = "5-1",
            ConversationId = "5",
            SenderId = "diana",
            SenderName = "Diana Prince",
            Content = "See you tomorrow",
            Timestamp = DateTime.Now.AddDays(-2),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForEngineeringTeam() =>
    [
        new ChatMessage
        {
            Id = "6-1",
            ConversationId = "6",
            SenderId = "engineer1",
            SenderName = "Tech Lead",
            Content = "@you Check the PR when you get a chance",
            Timestamp = DateTime.Now.AddDays(-3),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForFrank() =>
    [
        new ChatMessage
        {
            Id = "7-1",
            ConversationId = "7",
            SenderId = "me",
            SenderName = "You",
            Content = "How was the weekend?",
            Timestamp = DateTime.Now.AddDays(-7).AddHours(-1),
            IsSent = true,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        },
        new ChatMessage
        {
            Id = "7-2",
            ConversationId = "7",
            SenderId = "frank",
            SenderName = "Frank Castle",
            Content = "👍",
            Timestamp = DateTime.Now.AddDays(-7),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        }
    ];

    private List<ChatMessage> GenerateMessagesForGrace() =>
    [
        new ChatMessage
        {
            Id = "8-1",
            ConversationId = "8",
            SenderId = "grace",
            SenderName = "Grace Hopper",
            Content = "Debugging session?",
            Timestamp = DateTime.Now.AddDays(-14),
            IsSent = false,
            IsRead = true,
            Status = MessageStatus.Read,
            Type = MessageType.Text
        }
    ];

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            SendMessageCommand?.Dispose();
            AttachFileCommand?.Dispose();
            StartCallCommand?.Dispose();
            StartVideoCallCommand?.Dispose();
            ShowInfoCommand?.Dispose();
            ToggleEmojiCommand?.Dispose();
            StartVoiceRecordingCommand?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
