using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using ReactiveUI;

namespace Ecliptix.Feature.Splash.Splash.ViewModels;

public sealed class SplashWindowViewModel : Core.Core.MVVM.ViewModelBase
{
    private bool _isShuttingDown;

    public ConnectivitySnapshot Connectivity
    {
        get;
        private set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(ConnectivityStatus));
        }
    } = ConnectivitySnapshot.Initial with { Status = ConnectivityStatus.CONNECTING };

    public ConnectivityStatus ConnectivityStatus => Connectivity.Status;

    public TaskCompletionSource<bool> IsSubscribed { get; } = new();

    public SplashWindowViewModel(
        IConnectivityService connectivityService,
        ILocalizationService localizationService,
        NetworkProvider networkProvider,
        IGlobalModalService globalModalService)
        : base(networkProvider, localizationService, globalModalService, connectivityService)
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
