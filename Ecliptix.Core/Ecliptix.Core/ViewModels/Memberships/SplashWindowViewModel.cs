using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class SplashWindowViewModel : ViewModelBase, IActivatableViewModel
{
    private NetworkStatus _networkStatus = NetworkStatus.DataCenterConnecting;

    public NetworkStatus NetworkStatus
    {
        get => _networkStatus;
        private set => this.RaiseAndSetIfChanged(ref _networkStatus, value);
    }

    public string ApplicationVersion => VersionHelper.GetApplicationVersion();

    public ViewModelActivator Activator { get; } = new();

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();

    private string _statusText = "Initializing...";

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public SplashWindowViewModel(INetworkEvents networkEvents, ISystemEvents systemEvents)
    {
        this.WhenActivated(disposables =>
        {
            networkEvents.NetworkStatusChanged
                .Select(e => e.State)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(status =>
                {
                    NetworkStatus = status;

                    StatusText = status switch
                    {
                        NetworkStatus.DataCenterConnecting => "Connecting to data center...",
                        NetworkStatus.DataCenterConnected => "Connected to data center.",
                        NetworkStatus.DataCenterDisconnected => "Data center is not reachable.",
                        _ => "Unknown network status."
                    };
                })
                .DisposeWith(disposables);

            systemEvents.SystemStateChanged
                .Subscribe(systemStateChangedEvent => { StatusText = systemStateChangedEvent.State.ToString(); })
                .DisposeWith(disposables);

            IsSubscribed.TrySetResult(true);
        });
    }
}