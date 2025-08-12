using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class SplashWindowViewModel : ViewModelBase
{
    private static readonly Color ConnectingColor = Color.Parse("#FFBD2E");
    private static readonly Color ConnectedColor = Color.Parse("#28C940");
    private static readonly Color DisconnectedColor = Color.Parse("#FF5F57");
    private static readonly Color DefaultColor = Color.Parse("#9966CC");

    private NetworkStatus _networkStatus = NetworkStatus.DataCenterConnecting;
    private Color _glowColor = DefaultColor;
    private bool _isShuttingDown;

    public Color GlowColor
    {
        get => _glowColor;
        private set => this.RaiseAndSetIfChanged(ref _glowColor, value);
    }

    public NetworkStatus NetworkStatus
    {
        get => _networkStatus;
        private set => this.RaiseAndSetIfChanged(ref _networkStatus, value);
    }

    public TaskCompletionSource IsSubscribed { get; } = new();

    public SplashWindowViewModel(ISystemEvents systemEvents, INetworkEvents networkEvents, 
        ILocalizationService localizationService, NetworkProvider networkProvider) 
        : base(systemEvents, networkProvider, localizationService)
    {
        SetupNetworkStatusBinding(networkEvents);
    }

    private void SetupNetworkStatusBinding(INetworkEvents networkEvents)
    {
        this.WhenActivated(disposables =>
        {
            networkEvents.NetworkStatusChanged
                .Select(e => e.State)
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(new Action<NetworkStatus>(status =>
                {
                    if (_isShuttingDown) return;
                    NetworkStatus = status;
                    GlowColor = GetColorForStatus(status);
                }))
                .DisposeWith(disposables);

            IsSubscribed.TrySetResult();
        });
    }

    private static Color GetColorForStatus(NetworkStatus status) => status switch
    {
        NetworkStatus.DataCenterConnecting => ConnectingColor,
        NetworkStatus.RestoreSecrecyChannel => ConnectingColor,
        NetworkStatus.DataCenterConnected => ConnectedColor,
        NetworkStatus.DataCenterDisconnected => DisconnectedColor,
        _ => DefaultColor
    };

    public Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        return Task.CompletedTask;
    }
}