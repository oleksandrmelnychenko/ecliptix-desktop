using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Events;

namespace Ecliptix.Core.Messaging.Core.Messaging.Services;

public interface IConnectivityService : IDisposable
{
    ConnectivitySnapshot CurrentSnapshot { get; }

    ConnectivityStatus LastKnownInternetStatus { get; }

    ConnectivityStatus LastKnownServerStatus { get; }

    IObservable<ConnectivitySnapshot> ConnectivityStream { get; }

    IObservable<ConnectivityStatus> InternetStatus { get; }

    IObservable<ConnectivityStatus> ServerStatus { get; }

    Task PublishAsync(ConnectivityIntent intent, CancellationToken cancellationToken = default);

    Task RequestManualRetryAsync(uint? connectId = null);

    IDisposable OnManualRetryRequested(
        Func<ManualRetryRequestedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
