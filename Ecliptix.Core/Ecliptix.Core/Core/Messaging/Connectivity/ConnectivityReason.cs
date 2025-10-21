namespace Ecliptix.Core.Core.Messaging.Connectivity;

public enum ConnectivityReason
{
    None,
    HandshakeStarted,
    HandshakeSucceeded,
    RpcFailure,
    ManualRetry,
    Backoff,
    NoInternet,
    InternetRecovered,
    ServerShutdown,
    RetryLimitReached,
    OperationCancelled,
    HandshakeFailed,
    SecurityError,
    Unknown
}
