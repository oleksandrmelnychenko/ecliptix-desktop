using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public sealed class SplashWindowViewModel : ViewModelBase, IActivatableViewModel
{
    private NetworkStatus _networkStatus = NetworkStatus.DataCenterConnecting;
    private string _baseStatusText = "Establishing secure connection to data center";
    private string _dots = "";
    private bool _isShuttingDown;

    public NetworkStatus NetworkStatus
    {
        get => _networkStatus;
        private set => this.RaiseAndSetIfChanged(ref _networkStatus, value);
    }

    public string ApplicationVersion => VersionHelper.GetApplicationVersion();

    public ViewModelActivator Activator { get; } = new();

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();

    private Color _glowColor = Color.Parse("#9966CC"); 
    public Color GlowColor
    {
        get => _glowColor;
        private set => this.RaiseAndSetIfChanged(ref _glowColor, value);
    }
    
    public string BaseStatusText
    {
        get => _baseStatusText;
        private set => this.RaiseAndSetIfChanged(ref _baseStatusText, value);
    }

    public string Dots
    {
        get => _dots;
        private set => this.RaiseAndSetIfChanged(ref _dots, value);
    }

    public SplashWindowViewModel(INetworkEvents networkEvents, ISystemEvents systemEvents)
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

                    (BaseStatusText, GlowColor) = status switch
                    {
                        NetworkStatus.DataCenterConnecting => 
                            ("Establishing secure connection to data center", Color.Parse("#FFBD2E")),
                        NetworkStatus.RestoreSecrecyChannel => 
                            ("Restoring secure connection to data center", Color.Parse("#FFBD2E")),
                        NetworkStatus.DataCenterConnected => 
                            ("Connection established. Initializing services...", Color.Parse("#28C940")),
                        NetworkStatus.DataCenterDisconnected =>
                            ("Server not responding. Attempting to reconnect", Color.Parse("#FF5F57")),
                        _ => ("Unexpected network status.", Color.Parse("#9966CC"))
                    };

                    Dots = "";
                })
                .DisposeWith(disposables);

            systemEvents.SystemStateChanged
                .Delay(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(systemStateChangedEvent =>
                {
                    if (_isShuttingDown) return;

                    BaseStatusText = systemStateChangedEvent.State.ToString();
                    Dots = "";
                })
                .DisposeWith(disposables);

            var dotCount = 0;
            Observable.Interval(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (_isShuttingDown) return;

                    if (NetworkStatus == NetworkStatus.DataCenterConnecting || 
                        NetworkStatus == NetworkStatus.DataCenterDisconnected ||
                        NetworkStatus == NetworkStatus.RestoreSecrecyChannel)
                    {
                        dotCount = (dotCount + 1) % 4;
                        Dots = new string('.', dotCount);
                    }
                    else
                    {
                        Dots = "";
                    }
                })
                .DisposeWith(disposables);

            IsSubscribed.TrySetResult(true);
        });
    }

    public async Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        await Observable.Interval(TimeSpan.FromSeconds(1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Take(8)
            .Select(remaining => 7 - remaining)
            .Do(remaining => BaseStatusText = $"Shutting down in {remaining} seconds...")
            .LastAsync();
        _isShuttingDown = false;
    }
}