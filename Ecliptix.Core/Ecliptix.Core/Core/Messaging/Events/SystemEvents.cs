using System;

namespace Ecliptix.Core.Core.Messaging.Events;

/// <summary>
/// System application states
/// </summary>
public enum SystemState
{
    Initializing,
    Running,
    Busy,
    Recovering,
    DataCenterShutdown,
    FatalError
}

/// <summary>
/// Event fired when system state changes
/// Compatible with existing SystemStateChangedEvent from AppEvents
/// Converted from class to record for better immutability
/// </summary>
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