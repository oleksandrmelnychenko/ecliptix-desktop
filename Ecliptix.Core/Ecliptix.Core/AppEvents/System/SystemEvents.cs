using System;

namespace Ecliptix.Core.AppEvents.System;

public enum SystemState
{
    Initializing,
    Running,
    Busy,
    Degraded,
    FatalError
}

public class SystemStateChangedEvent
{
    public string State { get; private set; }

    private SystemStateChangedEvent(string state)
    {
        State = state;
    }

    public static SystemStateChangedEvent New(string state) => new(state);
}

public class SystemEvents(IEventAggregator aggregator) : ISystemEvents
{
    public IObservable<SystemStateChangedEvent> SystemStateChanged => aggregator.GetEvent<SystemStateChangedEvent>();
    public void Publish(SystemStateChangedEvent message) => aggregator.Publish(message);
}