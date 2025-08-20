using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Features.Splash.ViewModels;

public sealed class SplashWindowViewModel : Core.MVVM.ViewModelBase
{
    private static readonly Color ConnectingColor = Color.Parse("#FFBD2E");
    private static readonly Color ConnectedColor = Color.Parse("#28C940");
    private static readonly Color DisconnectedColor = Color.Parse("#FF5F57");
    private static readonly Color DefaultColor = Color.Parse("#9966CC");

    private static readonly FrozenDictionary<NetworkStatus, Color> StatusColorMap =
        new Dictionary<NetworkStatus, Color>
        {
            [NetworkStatus.DataCenterConnecting] = ConnectingColor,
            [NetworkStatus.RestoreSecrecyChannel] = ConnectingColor,
            [NetworkStatus.DataCenterConnected] = ConnectedColor,
            [NetworkStatus.DataCenterDisconnected] = DisconnectedColor
        }.ToFrozenDictionary();

    private static readonly Func<NetworkStatusChangedEvent, NetworkStatus> StateSelector = e => e.State;

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
        SetupPrecompiledNetworkBinding(networkEvents);
    }

    private void SetupPrecompiledNetworkBinding(INetworkEvents networkEvents)
    {
        this.WhenActivated(disposables =>
        {
            networkEvents.NetworkStatusChanged
                .Select(StateSelector)
                .DistinctUntilChanged()
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(ProcessNetworkStatusChange)
                .DisposeWith(disposables);

            IsSubscribed.TrySetResult();
        });
    }

    private void ProcessNetworkStatusChange(NetworkStatus status)
    {
        if (_isShuttingDown) return;
        NetworkStatus = status;
        GlowColor = GetColorForStatusFast(status);
    }

    private static Color GetColorForStatusFast(NetworkStatus status) =>
        StatusColorMap.GetValueOrDefault(status, DefaultColor);

    public Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        return Task.CompletedTask;
    }
}