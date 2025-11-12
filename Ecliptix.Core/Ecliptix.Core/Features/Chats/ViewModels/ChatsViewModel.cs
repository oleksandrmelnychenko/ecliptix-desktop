using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.ViewModels;

public sealed partial class ChatsViewModel : Core.MVVM.ViewModelBase
{
    private readonly CompositeDisposable _disposables = new();
    private bool _isDisposed;

    [Reactive] public string Title { get; set; }
    public ObservableCollection<string> Conversations { get; }

    public ChatsViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService)
        : base(networkProvider, localizationService, null)
    {
        Title = "Your Chats";
        Conversations = new ObservableCollection<string>
        {
            "No conversations yet..."
        };
    }

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
