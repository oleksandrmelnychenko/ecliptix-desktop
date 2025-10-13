using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Features.Splash.ViewModels;

public sealed class SplashWindowViewModel : Core.MVVM.ViewModelBase
{
    private NetworkStatus _networkStatus = NetworkStatus.DataCenterConnecting;
    private bool _isShuttingDown;

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
        IDisposable subscription = networkEventService.OnNetworkStatusChanged(evt =>
        {
            ProcessNetworkStatusChange(evt.State);
            return Task.CompletedTask;
        });

        this.WhenActivated(disposables =>
        {
            subscription.DisposeWith(disposables);
            IsSubscribed.TrySetResult();
        });
    }

    private void ProcessNetworkStatusChange(NetworkStatus status)
    {
        if (_isShuttingDown) return;
        NetworkStatus = status;
    }

    public Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        return Task.CompletedTask;
    }
}
