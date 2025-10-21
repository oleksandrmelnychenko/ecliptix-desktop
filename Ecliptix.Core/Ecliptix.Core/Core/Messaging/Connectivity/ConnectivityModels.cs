using System;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Core.Messaging.Connectivity;

public readonly record struct ConnectivitySnapshot(
    ConnectivityStatus Status,
    ConnectivityReason Reason,
    ConnectivitySource Source,
    NetworkFailure? Failure,
    uint? ConnectId,
    int? RetryAttempt,
    TimeSpan? RetryBackoff,
    Guid CorrelationId,
    DateTime OccurredAt)
{
    public static readonly ConnectivitySnapshot Initial = new(
        ConnectivityStatus.Connected,
        ConnectivityReason.None,
        ConnectivitySource.System,
        null,
        null,
        null,
        null,
        Guid.Empty,
        DateTime.MinValue);
}

public readonly record struct ConnectivityIntent(
    ConnectivityStatus Status,
    ConnectivityReason Reason,
    ConnectivitySource Source,
    NetworkFailure? Failure = null,
    uint? ConnectId = null,
    int? RetryAttempt = null,
    TimeSpan? RetryBackoff = null,
    Guid? CorrelationId = null)
{
    public static ConnectivityIntent Connected(uint? connectId = null, ConnectivityReason reason = ConnectivityReason.HandshakeSucceeded) =>
        new(ConnectivityStatus.Connected, reason, ConnectivitySource.DataCenter, null, connectId);

    public static ConnectivityIntent Connecting(uint? connectId = null, ConnectivityReason reason = ConnectivityReason.HandshakeStarted, ConnectivitySource source = ConnectivitySource.DataCenter) =>
        new(ConnectivityStatus.Connecting, reason, source, null, connectId);

    public static ConnectivityIntent Disconnected(NetworkFailure failure, uint? connectId = null, ConnectivityReason reason = ConnectivityReason.RpcFailure, ConnectivitySource source = ConnectivitySource.DataCenter) =>
        new(ConnectivityStatus.Disconnected, reason, source, failure, connectId);

    public static ConnectivityIntent Recovering(NetworkFailure failure, uint? connectId = null, int? retryAttempt = null, TimeSpan? backoff = null) =>
        new(ConnectivityStatus.Recovering, ConnectivityReason.Backoff, ConnectivitySource.System, failure, connectId, retryAttempt, backoff);

    public static ConnectivityIntent RetriesExhausted(NetworkFailure failure, uint? connectId = null, int? retries = null) =>
        new(ConnectivityStatus.RetriesExhausted, ConnectivityReason.RetryLimitReached, ConnectivitySource.System, failure, connectId, retries);

    public static ConnectivityIntent InternetLost() =>
        new(ConnectivityStatus.Unavailable, ConnectivityReason.NoInternet, ConnectivitySource.InternetProbe);

    public static ConnectivityIntent InternetRecovered() =>
        new(ConnectivityStatus.Connecting, ConnectivityReason.InternetRecovered, ConnectivitySource.InternetProbe);

    public static ConnectivityIntent ManualRetry(uint? connectId = null) =>
        new(ConnectivityStatus.Connecting, ConnectivityReason.ManualRetry, ConnectivitySource.ManualAction, null, connectId);

    public static ConnectivityIntent ServerShutdown(NetworkFailure failure) =>
        new(ConnectivityStatus.ShuttingDown, ConnectivityReason.ServerShutdown, ConnectivitySource.DataCenter, failure);
}
