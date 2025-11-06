using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface IConnectivityService : IDisposable
{
    ConnectivitySnapshot CurrentSnapshot { get; }

    IObservable<ConnectivitySnapshot> ConnectivityStream { get; }

    Task PublishAsync(ConnectivityIntent intent, CancellationToken cancellationToken = default);

    Task RequestManualRetryAsync(uint? connectId = null);

    IDisposable OnManualRetryRequested(
        Func<ManualRetryRequestedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
