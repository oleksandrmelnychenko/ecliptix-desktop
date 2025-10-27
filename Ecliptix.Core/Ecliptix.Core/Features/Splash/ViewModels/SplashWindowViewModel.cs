using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Features.Splash.ViewModels;

public sealed class SplashWindowViewModel : Core.MVVM.ViewModelBase
{
    private ConnectivitySnapshot _connectivity = ConnectivitySnapshot.Initial with { Status = ConnectivityStatus.Connecting };
    private bool _isShuttingDown;

    public ConnectivitySnapshot Connectivity
    {
        get => _connectivity;
        private set
        {
            this.RaiseAndSetIfChanged(ref _connectivity, value);
            this.RaisePropertyChanged(nameof(ConnectivityStatus));
        }
    }

    public ConnectivityStatus ConnectivityStatus => Connectivity.Status;

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();

    public SplashWindowViewModel(
        IConnectivityService connectivityService,
        ILocalizationService localizationService,
        NetworkProvider networkProvider)
        : base(networkProvider, localizationService)
    {
        SetupPrecompiledNetworkBinding(connectivityService);
    }

    public Task PrepareForShutdownAsync()
    {
        _isShuttingDown = true;
        return Task.CompletedTask;
    }

    private void SetupPrecompiledNetworkBinding(IConnectivityService connectivityService)
    {
        ProcessConnectivityChange(connectivityService.CurrentSnapshot);

        IDisposable subscription = connectivityService.ConnectivityStream.Subscribe(ProcessConnectivityChange);

        this.WhenActivated(disposables =>
        {
            subscription.DisposeWith(disposables);
            IsSubscribed.TrySetResult(true);
        });
    }

    private void ProcessConnectivityChange(ConnectivitySnapshot snapshot)
    {
        if (_isShuttingDown)
        {
            return;
        }

        Connectivity = snapshot;
    }
}
