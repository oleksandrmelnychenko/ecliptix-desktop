using System;

namespace Ecliptix.Core.AppEvents.Network;

public record NetworkStatusChangedEvent(NetworkStatus Status);

public class NetworkEvents(IEventAggregator aggregator) : INetworkEvents
{
    public IObservable<NetworkStatusChangedEvent> NetworkStatusChanged =>
        aggregator.GetEvent<NetworkStatusChangedEvent>();

    public void Publish(NetworkStatusChangedEvent message) => aggregator.Publish(message);
}
