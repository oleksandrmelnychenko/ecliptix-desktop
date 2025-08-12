using System;

namespace Ecliptix.Core.AppEvents.Network;

public interface INetworkEvents
{
    IObservable<NetworkStatusChangedEvent> NetworkStatusChanged { get; }
    IObservable<ManualRetryRequestedEvent> ManualRetryRequested { get; }
    void InitiateChangeState(NetworkStatusChangedEvent message);
    void RequestManualRetry(ManualRetryRequestedEvent message);
}