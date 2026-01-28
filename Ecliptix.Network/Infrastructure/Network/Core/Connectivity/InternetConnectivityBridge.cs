using System.Reactive.Linq;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Core;
using Serilog;

namespace Ecliptix.Network.Infrastructure.Network.Core.Connectivity;

public sealed class InternetConnectivityBridge : IDisposable
{
    private readonly IDisposable _subscription;
    private bool _disposed;

    public InternetConnectivityBridge(
        IInternetConnectivityObserver connectivityObserver,
        IConnectivityService connectivityService)
    {
        _subscription = connectivityObserver
            .Select(isOnline => isOnline
                ? ConnectivityIntent.InternetRecovered()
                : ConnectivityIntent.InternetLost())
            .Do(intent =>
            {
                Log.Information(
                    "[InternetConnectivityBridge] Processing intent: Status={Status}, Source={Source}",
                    intent.Status,
                    intent.Source);
            })
            .Select(intent => Observable.FromAsync(ct => connectivityService.PublishAsync(intent, ct)))
            .Concat()
            .Retry()
            .Subscribe(
                onNext: _ => { },
                onError: ex =>
                {
                    Log.Debug("[InternetConnectivityBridge] Error publishing connectivity intent: {Error}",
                            ex.Message);
                });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _subscription.Dispose();
    }
}
