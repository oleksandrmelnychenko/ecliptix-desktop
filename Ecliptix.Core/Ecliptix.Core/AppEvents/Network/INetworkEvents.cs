using System;

namespace Ecliptix.Core.AppEvents.Network;

public interface INetworkEvents
{
    IObservable<NetworkStatusChangedEvent> NetworkStatusChanged { get; }
    void Publish(NetworkStatusChangedEvent message);
}