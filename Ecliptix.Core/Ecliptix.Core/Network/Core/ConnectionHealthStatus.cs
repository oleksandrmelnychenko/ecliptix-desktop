namespace Ecliptix.Core.Network.Core;

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