using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class SplashWindowViewModel : ViewModelBase, IActivatableViewModel
{
    private NetworkStatus _networkStatus = NetworkStatus.DataCenterConnecting;
    
    public string ApplicationVersion => VersionHelper.GetApplicationVersion();

    public ViewModelActivator Activator { get; } = new();

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();
    
    private string _statusText  = "Initializing...";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }
    
    public SplashWindowViewModel(INetworkEvents networkEvents)
    {
        this.WhenActivated(disposables =>
        {
            networkEvents.NetworkStatusChanged
                .Subscribe(networkStatusChangedEvent =>
                {
                    _networkStatus = networkStatusChangedEvent.Status;
                })
                .DisposeWith(disposables); 
            
            IsSubscribed.TrySetResult(true);
        });
    }
}