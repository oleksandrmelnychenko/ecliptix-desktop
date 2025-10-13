using System;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum SystemState
{
    Initializing,
    Running,
    Busy,
    Recovering,
    DataCenterShutdown,
    FatalError
}

public sealed record SystemStateChangedEvent
{
    public SystemState State { get; }
    public string? LogMessage { get; }
    public DateTime Timestamp { get; }

    private SystemStateChangedEvent(SystemState state, string? logMessage = null)
    {
        State = state;
        LogMessage = logMessage;
        Timestamp = DateTime.UtcNow;
    }

    public static SystemStateChangedEvent New(SystemState state, string? logMessage = null) =>
        new(state, logMessage);
}
