namespace Ecliptix.Core.Infrastructure.Network.Core.State;

public enum ConnectionHealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
    Failed,
    Reconnecting,
    Disconnected
}