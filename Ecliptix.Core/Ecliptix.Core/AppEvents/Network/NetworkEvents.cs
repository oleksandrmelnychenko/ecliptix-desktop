using System;
using Ecliptix.Utilities;

namespace Ecliptix.Core.AppEvents.Network;

public record NetworkStatusChangedEvent
{
    public NetworkStatus State { get; }

    private NetworkStatusChangedEvent(NetworkStatus state)
    {
        State = state;
    }

    public static NetworkStatusChangedEvent New(NetworkStatus state) => new(state);
}

public record ManualRetryRequestedEvent
{
    public uint? ConnectId { get; }
    public DateTime RequestedAt { get; }

    private ManualRetryRequestedEvent(uint? connectId, DateTime requestedAt)
    {
        ConnectId = connectId;
        RequestedAt = requestedAt;
    }

    public static ManualRetryRequestedEvent New(uint? connectId = null) =>
        new(connectId, DateTime.UtcNow);
}

public class NetworkEvents(IEventAggregator aggregator) : INetworkEvents
{
    private Option<NetworkStatus> _currentState = Option<NetworkStatus>.None;

    public IObservable<NetworkStatusChangedEvent> NetworkStatusChanged =>
        aggregator.GetEvent<NetworkStatusChangedEvent>();

    public IObservable<ManualRetryRequestedEvent> ManualRetryRequested =>
        aggregator.GetEvent<ManualRetryRequestedEvent>();

    public void InitiateChangeState(NetworkStatusChangedEvent message)
    {
        
        if (_currentState.HasValue && _currentState.Value == message.State)
            return;

        _currentState = Option<NetworkStatus>.Some(message.State);
        aggregator.Publish(message);
    }

    public void RequestManualRetry(ManualRetryRequestedEvent message)
    {
        aggregator.Publish(message);
    }
}