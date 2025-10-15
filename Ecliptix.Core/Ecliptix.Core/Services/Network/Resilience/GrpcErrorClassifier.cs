using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Resilience;

public static class GrpcErrorClassifier
{
    public static bool IsBusinessError(RpcException ex) =>
        ex.StatusCode is
            StatusCode.InvalidArgument or
            StatusCode.NotFound or
            StatusCode.AlreadyExists or
            StatusCode.FailedPrecondition or
            StatusCode.OutOfRange or
            StatusCode.Unimplemented;

    public static bool IsAuthenticationError(RpcException ex) =>
        ex.StatusCode is
            StatusCode.Unauthenticated or
            StatusCode.PermissionDenied;

    public static bool IsTransientInfrastructure(RpcException ex) =>
        ex.StatusCode is
            StatusCode.DeadlineExceeded or
            StatusCode.ResourceExhausted or
            StatusCode.Aborted;

    public static bool RequiresHandshakeRecovery(RpcException ex) =>
        ex.StatusCode is
            StatusCode.Unavailable or
            StatusCode.Internal or
            StatusCode.Unknown or
            StatusCode.DataLoss;

    public static bool IsProtocolStateMismatch(RpcException ex)
    {
        if (ex.StatusCode != StatusCode.Internal)
            return false;

        string detail = ex.Status.Detail?.ToLowerInvariant() ?? string.Empty;

        return detail.Contains("requested index") && detail.Contains("not future") ||
               detail.Contains("chain rotation") ||
               detail.Contains("sequence mismatch") ||
               detail.Contains("protocol state") && detail.Contains("mismatch") ||
               detail.Contains("dhpublic") && detail.Contains("unknown") ||
               detail.Contains("sender chain") && detail.Contains("invalid") ||
               detail.Contains("receiver chain") && detail.Contains("invalid") ||
               detail.Contains("protocol version") ||
               detail.Contains("state version") ||
               detail.Contains("channel state") && detail.Contains("invalid");
    }

    public static bool IsServerShutdown(RpcException ex) =>
        ex.StatusCode == StatusCode.Unavailable &&
        (ex.Status.Detail?.Contains("shutdown", System.StringComparison.OrdinalIgnoreCase) == true ||
         ex.Status.Detail?.Contains("maintenance", System.StringComparison.OrdinalIgnoreCase) == true);

    public static bool IsCancelled(RpcException ex) =>
        ex.StatusCode == StatusCode.Cancelled;

    public static bool IsIdentityKeyDerivationFailure(RpcException ex) =>
        ex.StatusCode == StatusCode.Unauthenticated &&
        (ex.Status.Detail?.Contains("IDENTITY_KEY_DERIVATION_FAILED", System.StringComparison.Ordinal) == true);
}
