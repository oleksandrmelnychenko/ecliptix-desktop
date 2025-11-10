using System;
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

public sealed class ChatListViewModel : Ecliptix.Core.Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public string SearchText { get; set; } = string.Empty;
    [Reactive] public ChatConversation? SelectedConversation { get; set; }

    public ObservableCollection<ChatConversation> Conversations { get; }
    public ObservableCollection<ChatConversation> FilteredConversations { get; }

    public ReactiveCommand<ChatConversation, Unit> SelectConversationCommand { get; }

    public ChatListViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        Conversations = new ObservableCollection<ChatConversation>(GenerateMockConversations());
        FilteredConversations = new ObservableCollection<ChatConversation>(Conversations);

        SelectConversationCommand = ReactiveCommand.Create<ChatConversation>(conversation =>
        {
            if (SelectedConversation != null)
            {
                SelectedConversation.IsSelected = false;
            }

            SelectedConversation = conversation;
            if (conversation != null)
            {
                conversation.IsSelected = true;
            }
        });

        this.WhenAnyValue(x => x.SearchText)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(searchText =>
            {
                FilteredConversations.Clear();
                ObservableCollection<ChatConversation> filtered = string.IsNullOrWhiteSpace(searchText)
                    ? Conversations
                    : new ObservableCollection<ChatConversation>(
                        Conversations.Where(c => c.ContactName.Contains(searchText, StringComparison.OrdinalIgnoreCase) ||
                                                c.LastMessage.Contains(searchText, StringComparison.OrdinalIgnoreCase)));

                foreach (ChatConversation conversation in filtered)
                {
                    FilteredConversations.Add(conversation);
                }
            })
            .DisposeWith(_disposables);

        if (Conversations.Count > 0)
        {
            SelectConversationCommand.Execute(Conversations[0]).Subscribe().DisposeWith(_disposables);
        }
    }

    private ChatConversation[] GenerateMockConversations() =>
    [
        new ChatConversation
        {
            Id = "1",
            ContactName = "Alice Johnson",
            ContactAvatar = "AJ",
            LastMessage = "Hey, how are you?",
            LastMessageTime = DateTime.Now.AddMinutes(-2),
            UnreadCount = 2,
            IsOnline = true,
            IsPinned = false,
            Type = ConversationType.Direct
        },
        new ChatConversation
        {
            Id = "2",
            ContactName = "Bob Smith",
            ContactAvatar = "BS",
            LastMessage = "Meeting at 3pm",
            LastMessageTime = DateTime.Now.AddHours(-1),
            UnreadCount = 0,
            IsOnline = false,
            IsPinned = false,
            Type = ConversationType.Direct
        },
        new ChatConversation
        {
            Id = "3",
            ContactName = "Team Design",
            ContactAvatar = "TD",
            LastMessage = "New mockups ready",
            LastMessageTime = DateTime.Now.AddHours(-3),
            UnreadCount = 5,
            IsOnline = false,
            IsPinned = false,
            Type = ConversationType.Group
        },
        new ChatConversation
        {
            Id = "4",
            ContactName = "Charlie Brown",
            ContactAvatar = "CB",
            LastMessage = "Thanks!",
            LastMessageTime = DateTime.Now.AddDays(-1),
            UnreadCount = 0,
            IsOnline = true,
            IsPinned = false,
            Type = ConversationType.Direct
        },
        new ChatConversation
        {
            Id = "5",
            ContactName = "Diana Prince",
            ContactAvatar = "DP",
            LastMessage = "See you tomorrow",
            LastMessageTime = DateTime.Now.AddDays(-2),
            UnreadCount = 0,
            IsOnline = false,
            IsPinned = false,
            Type = ConversationType.Direct
        },
        new ChatConversation
        {
            Id = "6",
            ContactName = "Engineering Team",
            ContactAvatar = "ET",
            LastMessage = "@you Check the PR",
            LastMessageTime = DateTime.Now.AddDays(-3),
            UnreadCount = 0,
            IsOnline = false,
            IsPinned = true,
            Type = ConversationType.Group
        },
        new ChatConversation
        {
            Id = "7",
            ContactName = "Frank Castle",
            ContactAvatar = "FC",
            LastMessage = "👍",
            LastMessageTime = DateTime.Now.AddDays(-7),
            UnreadCount = 0,
            IsOnline = false,
            IsPinned = false,
            Type = ConversationType.Direct
        },
        new ChatConversation
        {
            Id = "8",
            ContactName = "Grace Hopper",
            ContactAvatar = "GH",
            LastMessage = "Debugging session?",
            LastMessageTime = DateTime.Now.AddDays(-14),
            UnreadCount = 0,
            IsOnline = true,
            IsPinned = false,
            Type = ConversationType.Direct
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
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
