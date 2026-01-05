namespace Ecliptix.Core.Messaging.Core.Messaging.Connectivity;

public enum ConnectivityStatus
{
    CONNECTED,
    CONNECTING,
    DISCONNECTED,
    RECOVERING,
    UNAVAILABLE,
    SHUTTING_DOWN,
    RETRIES_EXHAUSTED
}
