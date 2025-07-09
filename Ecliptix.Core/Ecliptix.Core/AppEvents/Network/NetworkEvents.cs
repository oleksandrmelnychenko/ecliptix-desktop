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

public class NetworkEvents(IEventAggregator aggregator) : INetworkEvents
{
    private Option<NetworkStatus> _currentState = Option<NetworkStatus>.None;

    public IObservable<NetworkStatusChangedEvent> NetworkStatusChanged =>
        aggregator.GetEvent<NetworkStatusChangedEvent>();

    public void InitiateChangeState(NetworkStatusChangedEvent message)
    {
        if (_currentState != Option<NetworkStatus>.None &&
            (!_currentState.HasValue || _currentState.Value == message.State)) return;
        
        _currentState = Option<NetworkStatus>.Some(message.State);
        aggregator.Publish(message);
    }
}