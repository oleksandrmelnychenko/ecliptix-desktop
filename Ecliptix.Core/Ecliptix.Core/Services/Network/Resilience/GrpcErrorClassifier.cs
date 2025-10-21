using System;
using System.Linq;
using Ecliptix.Core.Infrastructure.Network.Core.Constants;
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
            StatusCode.Unavailable or
            StatusCode.Cancelled;

    public static bool RequiresHandshakeRecovery(RpcException ex) =>
        ex.StatusCode is
            StatusCode.Internal or
            StatusCode.Unknown or
            StatusCode.DataLoss;

    public static bool IsProtocolStateMismatch(RpcException ex)
    {
        if (ex.StatusCode != StatusCode.Internal && ex.StatusCode != StatusCode.FailedPrecondition)
            return false;

        string detail = ex.Status.Detail?.ToLowerInvariant() ?? string.Empty;

        return detail.Contains("header authentication failed") ||
               detail.Contains("requested index") && detail.Contains("not future") ||
               detail.Contains("chain rotation") ||
               detail.Contains("sequence mismatch") ||
               detail.Contains("protocol state") && detail.Contains("mismatch") ||
               detail.Contains("protocol state") && detail.Contains("desynchronized") ||
               detail.Contains("dhpublic") && detail.Contains("unknown") ||
               detail.Contains("sender chain") && detail.Contains("invalid") ||
               detail.Contains("receiver chain") && detail.Contains("invalid") ||
               detail.Contains("protocol version") ||
               detail.Contains("state version") ||
               detail.Contains("channel state") && detail.Contains("invalid");
    }

    public static bool IsServerShutdown(RpcException ex) =>
        ex.StatusCode == StatusCode.Unavailable &&
        (ex.Status.Detail?.Contains("shutdown", StringComparison.OrdinalIgnoreCase) == true ||
         ex.Status.Detail?.Contains("maintenance", StringComparison.OrdinalIgnoreCase) == true);

    public static bool IsCancelled(RpcException ex) =>
        ex.StatusCode == StatusCode.Cancelled;

    public static bool IsIdentityKeyDerivationFailure(RpcException ex) =>
        ex.StatusCode == StatusCode.Unauthenticated &&
        (ex.Status.Detail?.Contains("IDENTITY_KEY_DERIVATION_FAILED", System.StringComparison.Ordinal) == true);

    public static bool IsAuthFlowMissing(RpcException ex)
    {
        if (ex.StatusCode != StatusCode.NotFound)
        {
            return false;
        }

        Metadata trailers = ex.Trailers;
        string? errorCode = trailers.FirstOrDefault(entry =>
            string.Equals(entry.Key, GrpcErrorMetadataKeys.ErrorCode, StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.Equals(errorCode, "AuthFlowMissing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? i18nKey = trailers.FirstOrDefault(entry =>
            string.Equals(entry.Key, GrpcErrorMetadataKeys.I18nKey, StringComparison.OrdinalIgnoreCase))?.Value;
        return string.Equals(i18nKey, "error.auth_flow_missing", StringComparison.OrdinalIgnoreCase);
    }
}
