using System;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Core.Messaging.Connectivity;

public readonly record struct ConnectivitySnapshot(
    ConnectivityStatus Status,
    ConnectivityReason Reason,
    ConnectivitySource Source,
    int? RetryAttempt,
    Guid CorrelationId)
{
    public static readonly ConnectivitySnapshot Initial = new(
        ConnectivityStatus.CONNECTED,
        ConnectivityReason.NONE,
        ConnectivitySource.SYSTEM,
        null,
        Guid.Empty);
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
    public static ConnectivityIntent Connected(uint? connectId = null, ConnectivityReason reason = ConnectivityReason.HANDSHAKE_SUCCEEDED) =>
        new(ConnectivityStatus.CONNECTED, reason, ConnectivitySource.DATA_CENTER, null, connectId);

    public static ConnectivityIntent Connecting(uint? connectId = null, ConnectivityReason reason = ConnectivityReason.HANDSHAKE_STARTED, ConnectivitySource source = ConnectivitySource.DATA_CENTER) =>
        new(ConnectivityStatus.CONNECTING, reason, source, null, connectId);

    public static ConnectivityIntent Disconnected(NetworkFailure failure, uint? connectId = null, ConnectivityReason reason = ConnectivityReason.RPC_FAILURE, ConnectivitySource source = ConnectivitySource.DATA_CENTER) =>
        new(ConnectivityStatus.DISCONNECTED, reason, source, failure, connectId);

    public static ConnectivityIntent Recovering(NetworkFailure failure, uint? connectId = null, int? retryAttempt = null, TimeSpan? backoff = null) =>
        new(ConnectivityStatus.RECOVERING, ConnectivityReason.BACKOFF, ConnectivitySource.SYSTEM, failure, connectId, retryAttempt, backoff);

    public static ConnectivityIntent RetriesExhausted(NetworkFailure failure, uint? connectId = null, int? retries = null) =>
        new(ConnectivityStatus.RETRIES_EXHAUSTED, ConnectivityReason.RETRY_LIMIT_REACHED, ConnectivitySource.SYSTEM, failure, connectId, retries);

    public static ConnectivityIntent InternetLost() =>
        new(ConnectivityStatus.UNAVAILABLE, ConnectivityReason.NO_INTERNET, ConnectivitySource.INTERNET_PROBE);

    public static ConnectivityIntent InternetRecovered() =>
        new(ConnectivityStatus.CONNECTING, ConnectivityReason.INTERNET_RECOVERED, ConnectivitySource.INTERNET_PROBE);

    public static ConnectivityIntent ManualRetry(uint? connectId = null) =>
        new(ConnectivityStatus.CONNECTING, ConnectivityReason.MANUAL_RETRY, ConnectivitySource.MANUAL_ACTION, null, connectId);

    public static ConnectivityIntent ServerShutdown(NetworkFailure failure) =>
        new(ConnectivityStatus.SHUTTING_DOWN, ConnectivityReason.SERVER_SHUTDOWN, ConnectivitySource.DATA_CENTER, failure);
}
