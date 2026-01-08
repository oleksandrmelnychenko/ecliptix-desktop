using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Core;

namespace Ecliptix.Network.Infrastructure.Network.Core.Connectivity;

public sealed class InternetConnectivityBridge : IDisposable
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
