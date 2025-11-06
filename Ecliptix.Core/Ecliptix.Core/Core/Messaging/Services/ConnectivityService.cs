using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

internal sealed class ConnectivityService(IMessageBus messageBus) : IConnectivityService
{
    private readonly ConnectivityPublisher _connectivityPublisher = new();
    private bool _disposed;

    public ConnectivitySnapshot CurrentSnapshot => _connectivityPublisher.CurrentSnapshot;

    public IObservable<ConnectivitySnapshot> ConnectivityStream => _connectivityPublisher.ConnectivityStream;

    public Task PublishAsync(ConnectivityIntent intent, CancellationToken cancellationToken = default)
    {
        return _disposed
            ? throw new ObjectDisposedException(nameof(ConnectivityService))
            : _connectivityPublisher.PublishAsync(intent, cancellationToken);
    }

    public Task RequestManualRetryAsync(uint? connectId = null)
    {
        if (!_disposed)
        {
            ManualRetryRequestedEvent evt = ManualRetryRequestedEvent.New(connectId);
            return PublishManualRetryAsync(evt);
        }

        throw new ObjectDisposedException(nameof(ConnectivityService));
    }

    private async Task PublishManualRetryAsync(ManualRetryRequestedEvent evt)
    {
        await messageBus.PublishAsync(evt).ConfigureAwait(false);
        await PublishAsync(ConnectivityIntent.ManualRetry(evt.ConnectId)).ConfigureAwait(false);
    }

    public IDisposable OnManualRetryRequested(
        Func<ManualRetryRequestedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) =>
        messageBus.Subscribe(handler, lifetime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectivityPublisher.Dispose();
    }
}
