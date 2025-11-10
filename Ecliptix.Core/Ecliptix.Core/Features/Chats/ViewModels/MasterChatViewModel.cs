using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;

using ReactiveUI;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public sealed class MasterChatViewModel : Ecliptix.Core.Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    public ChatListViewModel ChatList { get; }
    public ChatWindowViewModel ChatWindow { get; }
    public ContactDetailsViewModel ContactDetails { get; }

    public MasterChatViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        ChatList = new ChatListViewModel(networkProvider, localizationService);
        ChatWindow = new ChatWindowViewModel(networkProvider, localizationService);
        ContactDetails = new ContactDetailsViewModel(networkProvider, localizationService);

        ChatList.WhenAnyValue(x => x.SelectedConversation)
            .WhereNotNull()
            .Subscribe(conversation =>
            {
                ChatWindow.SelectedConversation = conversation;
            })
            .DisposeWith(_disposables);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            ChatList?.Dispose();
            ChatWindow?.Dispose();
            ContactDetails?.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
