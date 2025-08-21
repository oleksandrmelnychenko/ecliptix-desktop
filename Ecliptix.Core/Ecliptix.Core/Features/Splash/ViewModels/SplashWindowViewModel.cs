using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia.Media;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
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

    public SplashWindowViewModel(ISystemEventService systemEventService, INetworkEventService networkEventService,
        ILocalizationService localizationService, NetworkProvider networkProvider)
        : base(systemEventService, networkProvider, localizationService)
    {
        SetupPrecompiledNetworkBinding(networkEventService);
    }

    private void SetupPrecompiledNetworkBinding(INetworkEventService networkEventService)
    {
        this.WhenActivated(disposables =>
        {
            IDisposable subscription = networkEventService.OnNetworkStatusChanged(evt =>
            {
                ProcessNetworkStatusChange(evt.State);
                return Task.CompletedTask;
            });

            subscription.DisposeWith(disposables);
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