using System;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum NetworkStatus
{
    DataCenterConnected,
    DataCenterDisconnected,
    DataCenterConnecting,
    RestoreSecrecyChannel,
    RetriesExhausted,
    ServerShutdown,
    ConnectionRecovering,
    ConnectionRestored,
    NoInternet
}

public sealed record NetworkStatusChangedEvent
{
    public NetworkStatus State { get; }
    public DateTime Timestamp { get; }

    private NetworkStatusChangedEvent(NetworkStatus state)
    {
        State = state;
        Timestamp = DateTime.UtcNow;
    }

    public static NetworkStatusChangedEvent New(NetworkStatus state) => new(state);
}

public sealed record ManualRetryRequestedEvent
{
    public uint? ConnectId { get; }
    public DateTime RequestedAt { get; }

    private ManualRetryRequestedEvent(uint? connectId, DateTime requestedAt)
    {
        ConnectId = connectId;
        RequestedAt = requestedAt;
    }

    public static ManualRetryRequestedEvent New(uint? connectId = null) =>
        new(connectId, DateTime.UtcNow);
}
