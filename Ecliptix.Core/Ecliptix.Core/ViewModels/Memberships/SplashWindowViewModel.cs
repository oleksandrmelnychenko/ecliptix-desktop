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
    private string _baseStatusText = "Initializing...";
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

    public string StatusText => _isShuttingDown ? _baseStatusText : _baseStatusText + _dots; // Combine dynamically

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
                .Delay(TimeSpan.FromSeconds(1)) // 1-second delay for UX
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(status =>
                {
                    if (_isShuttingDown) return; // Skip status updates during shutdown

                    NetworkStatus = status;

                    BaseStatusText = status switch
                    {
                        NetworkStatus.DataCenterConnecting => "Establishing secure connection to data center",
                        NetworkStatus.DataCenterConnected => "Connection established. Initializing services...",
                        NetworkStatus.DataCenterDisconnected =>
                            "Server not responding. Attempting to reconnect",
                        _ => "Unexpected network status. Contact support if this persists."
                    };

                    Dots = ""; // Reset dots on status change
                })
                .DisposeWith(disposables);

            systemEvents.SystemStateChanged
                .Delay(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(systemStateChangedEvent =>
                {
                    if (_isShuttingDown) return; // Skip during shutdown

                    BaseStatusText = systemStateChangedEvent.State.ToString();
                    Dots = "";
                })
                .DisposeWith(disposables);

            // Dot animation for connecting or disconnected states
            var dotCount = 0;
            Observable.Interval(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (_isShuttingDown) return; // No dots during shutdown

                    if (NetworkStatus == NetworkStatus.DataCenterConnecting ||
                        NetworkStatus == NetworkStatus.DataCenterDisconnected)
                    {
                        dotCount = (dotCount + 1) % 4; // Cycle 0-3 dots
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
        _isShuttingDown = true; // Block other status updates
        await Observable.Interval(TimeSpan.FromSeconds(1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Take(8) // 8 ticks for 7 to 0
            .Select(remaining => 7 - remaining)
            .Do(remaining => BaseStatusText = $"Shutting down in {remaining} seconds...") // Update base text
            .LastAsync();
        _isShuttingDown = false; // Reset flag
    }
}