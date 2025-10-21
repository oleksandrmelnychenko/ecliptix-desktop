using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;

namespace Ecliptix.Core.Infrastructure.Network.Core.Connectivity;

internal sealed class InternetConnectivityBridge : IDisposable
{
    private readonly IConnectivityService _connectivityService;
    private readonly IDisposable _subscription;
    private bool _disposed;

    public InternetConnectivityBridge(
        IInternetConnectivityObserver connectivityObserver,
        IConnectivityService connectivityService)
    {
        _connectivityService = connectivityService;

        _subscription = connectivityObserver.Subscribe(async isOnline =>
        {
            if (_disposed) return;

            ConnectivityIntent intent = isOnline
                ? ConnectivityIntent.InternetRecovered()
                : ConnectivityIntent.InternetLost();

            await _connectivityService.PublishAsync(intent).ConfigureAwait(false);
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _subscription.Dispose();
    }
}
