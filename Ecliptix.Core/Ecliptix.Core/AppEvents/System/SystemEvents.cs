using System;

namespace Ecliptix.Core.AppEvents.System;

public enum SystemState
{
    Initializing,
    Running,
    Busy,
    DataCenterShutdown,
    FatalError
}

public class SystemStateChangedEvent
{
    public SystemState State { get; private set; }
    public string? LogMessage { get; }

    private SystemStateChangedEvent(SystemState state, string? logMessage = null)
    {
        State = state;
        LogMessage = logMessage;
    }

    public static SystemStateChangedEvent New(SystemState state, string? logMessage = null) => new(state, logMessage);
}

public class SystemEvents(IEventAggregator aggregator) : ISystemEvents
{
    public IObservable<SystemStateChangedEvent> SystemStateChanged => aggregator.GetEvent<SystemStateChangedEvent>();
    public void Publish(SystemStateChangedEvent message) => aggregator.Publish(message);
}