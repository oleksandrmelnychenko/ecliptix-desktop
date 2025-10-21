using System;

namespace Ecliptix.Core.Core.Messaging.Events;

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
