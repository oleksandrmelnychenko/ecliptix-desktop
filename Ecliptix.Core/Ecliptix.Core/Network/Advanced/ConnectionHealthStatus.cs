namespace Ecliptix.Core.Network.Advanced;

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