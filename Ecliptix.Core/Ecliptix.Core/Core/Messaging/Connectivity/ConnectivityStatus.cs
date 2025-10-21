namespace Ecliptix.Core.Core.Messaging.Connectivity;

public enum ConnectivityStatus
{
    Connected,
    Connecting,
    Disconnected,
    Recovering,
    Unavailable,
    ShuttingDown,
    RetriesExhausted
}
