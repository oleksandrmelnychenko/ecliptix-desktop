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
    private bool _isShuttingDown;
    private string _baseSubtitle = "";

    private string _titleText = "Ecliptix";

    public string TitleText
    {
        get => _titleText;
        private set => this.RaiseAndSetIfChanged(ref _titleText, value);
    }

    private string _subtitleText = "Establishing secure connection...";

    public string SubtitleText
    {
        get => _subtitleText;
        private set => this.RaiseAndSetIfChanged(ref _subtitleText, value);
    }

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

    public string ApplicationVersion => VersionHelper.GetApplicationVersion();
    public ViewModelActivator Activator { get; } = new();
    public TaskCompletionSource<bool> IsSubscribed { get; } = new();

    public SplashWindowViewModel(INetworkEvents networkEvents, ISystemEvents systemEvents)
    {
        this.WhenActivated((CompositeDisposable disposables) =>
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

            systemEvents.SystemStateChanged
                .Delay(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(systemStateChangedEvent =>
                {
                    if (_isShuttingDown) return;
                    TitleText = "System Update";
                    _baseSubtitle = systemStateChangedEvent.State.ToString();
                    SubtitleText = _baseSubtitle;
                })
                .DisposeWith(disposables);

            int dotCount = 0;
            Observable.Interval(TimeSpan.FromSeconds(1))
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (_isShuttingDown) return;
                    if (NetworkStatus is NetworkStatus.DataCenterConnecting or NetworkStatus.DataCenterDisconnected
                        or NetworkStatus.RestoreSecrecyChannel)
                    {
                        dotCount = (dotCount + 1) % 4;
                        SubtitleText = _baseSubtitle + new string('.', dotCount);
                    }
                })
                .DisposeWith(disposables);

            IsSubscribed.TrySetResult(true);
        });
    }

    private void UpdateUiForNetworkStatus(NetworkStatus status)
    {
        (string title, string baseSubtitle, Color glowColor) = status switch
        {
            NetworkStatus.DataCenterConnecting =>
                ("Connecting", "Establishing secure connection", Color.Parse("#FFBD2E")),
            NetworkStatus.RestoreSecrecyChannel =>
                ("Reconnecting", "Restoring secure channel", Color.Parse("#FFBD2E")),
            NetworkStatus.DataCenterConnected =>
                ("Connected", "Initializing services...", Color.Parse("#28C940")),
            NetworkStatus.DataCenterDisconnected =>
                ("Server not responding", "Attempting to reconnect", Color.Parse("#FF5F57")),
            _ => ("Status Unknown", "Unexpected network status.", Color.Parse("#9966CC"))
        };

        TitleText = title;
        _baseSubtitle = baseSubtitle;
        GlowColor = glowColor;

        SubtitleText = _baseSubtitle;
    }

    public async Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        await Observable.Interval(TimeSpan.FromSeconds(1))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Take(8)
            .Select(remaining => 7 - remaining)
            .Do(remaining =>
            {
                TitleText = "Shutting Down";
                SubtitleText = $"Closing in {remaining} seconds...";
            })
            .LastAsync();
        _isShuttingDown = false;
    }
}