using System;

namespace Ecliptix.Core.Infrastructure.Network.Core.State;

public record ConnectionHealth
{
    public uint ConnectId { get; init; }
    public ConnectionHealthStatus Status { get; init; } = ConnectionHealthStatus.Unknown;
    public ConnectionHealthMetrics Metrics { get; init; } = new();
    public DateTime LastHealthCheck { get; init; } = DateTime.UtcNow;
    public bool IsRecovering { get; init; }
    public int RecoveryAttempts { get; init; }
    public string? LastError { get; init; }
}