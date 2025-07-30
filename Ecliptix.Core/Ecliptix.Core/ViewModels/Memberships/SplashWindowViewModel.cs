using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class SplashWindowViewModel : ViewModelBase
{
    private NetworkStatus _networkStatus = NetworkStatus.DataCenterConnecting;
    private bool _isShuttingDown;
    private Color _glowColor = Color.Parse("#9966CC");

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

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();

    public SplashWindowViewModel(ISystemEvents systemEvents,INetworkEvents networkEvents, ILocalizationService localizationService,
        NetworkProvider networkProvider) : base(systemEvents,networkProvider, localizationService)
    {
        this.WhenActivated(disposables =>
        {
            networkEvents.NetworkStatusChanged
                .Select(e => e.State)
                .Delay(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(status =>
                {
                    if (_isShuttingDown) return;
                    NetworkStatus = status;
                    UpdateUiForNetworkStatus(status);
                })
                .DisposeWith(disposables);

            IsSubscribed.TrySetResult(true);
        });
    }

    public async Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        await Task.Delay(TimeSpan.FromSeconds(8));
        _isShuttingDown = false;
    }

    private void UpdateUiForNetworkStatus(NetworkStatus status)
    {
        GlowColor = status switch
        {
            NetworkStatus.DataCenterConnecting => Color.Parse("#FFBD2E"),
            NetworkStatus.RestoreSecrecyChannel => Color.Parse("#FFBD2E"),
            NetworkStatus.DataCenterConnected => Color.Parse("#28C940"),
            NetworkStatus.DataCenterDisconnected => Color.Parse("#FF5F57"),
            _ => Color.Parse("#9966CC")
        };
    }
}