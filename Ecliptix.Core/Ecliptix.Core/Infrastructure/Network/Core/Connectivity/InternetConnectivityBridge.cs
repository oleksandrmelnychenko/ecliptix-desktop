using System;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;

namespace Ecliptix.Core.Infrastructure.Network.Core.Connectivity;

internal sealed class InternetConnectivityBridge : IDisposable
{
    private readonly IDisposable _subscription;
    private bool _disposed;

    public InternetConnectivityBridge(
        IInternetConnectivityObserver connectivityObserver,
        IConnectivityService connectivityService)
    {
        IConnectivityService connectivityService1 = connectivityService;

        _subscription = connectivityObserver.Subscribe(async void (isOnline) =>
        {
            try
            {
                if (_disposed)
                {
                    return;
                }

                ConnectivityIntent intent = isOnline
                    ? ConnectivityIntent.InternetRecovered()
                    : ConnectivityIntent.InternetLost();

                await connectivityService1.PublishAsync(intent).ConfigureAwait(false);
            }
            catch
            {
                //Suppress
            }
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
