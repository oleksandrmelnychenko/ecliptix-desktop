using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

internal sealed class ConnectivityService : IConnectivityService
{
    private readonly IMessageBus _messageBus;
    private readonly ConnectivityPublisher _connectivityPublisher = new();
    private readonly IDisposable _internalSubscription;
    private bool _disposed;

    private readonly BehaviorSubject<ConnectivityStatus> _internetSubject
        = new(ConnectivityStatus.UNAVAILABLE);

    private readonly BehaviorSubject<ConnectivityStatus> _serverSubject
        = new(ConnectivityStatus.DISCONNECTED);

    public ConnectivitySnapshot CurrentSnapshot => _connectivityPublisher.CurrentSnapshot;

    public ConnectivityStatus LastKnownInternetStatus => _internetSubject.Value;
    public ConnectivityStatus LastKnownServerStatus => _serverSubject.Value;

    public IObservable<ConnectivitySnapshot> ConnectivityStream => _connectivityPublisher.ConnectivityStream;

    public IObservable<ConnectivityStatus> InternetStatus => _internetSubject.AsObservable();
    public IObservable<ConnectivityStatus> ServerStatus => _serverSubject.AsObservable();

    public ConnectivityService(IMessageBus messageBus)
    {
        _messageBus = messageBus;

        _internalSubscription = _connectivityPublisher.ConnectivityStream.Subscribe(UpdateInternalState);
    }

    private void UpdateInternalState(ConnectivitySnapshot snapshot)
    {
        if (snapshot.Source == ConnectivitySource.INTERNET_PROBE)
        {
            if (_internetSubject.Value != snapshot.Status)
            {
                _internetSubject.OnNext(snapshot.Status);
            }
        }
        else if (snapshot.Source == ConnectivitySource.DATA_CENTER)
        {
            if (_serverSubject.Value != snapshot.Status)
            {
                _serverSubject.OnNext(snapshot.Status);
            }
        }
    }

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
        await _messageBus.PublishAsync(evt).ConfigureAwait(false);
        await PublishAsync(ConnectivityIntent.ManualRetry(evt.ConnectId)).ConfigureAwait(false);
    }

    public IDisposable OnManualRetryRequested(
        Func<ManualRetryRequestedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) =>
        _messageBus.Subscribe(handler, lifetime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _internalSubscription.Dispose();

        _internetSubject.OnCompleted();
        _internetSubject.Dispose();

        _serverSubject.OnCompleted();
        _serverSubject.Dispose();

        _connectivityPublisher.Dispose();
    }
}
