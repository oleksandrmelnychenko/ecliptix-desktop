using Ecliptix.Network.Infrastructure.Network.Core.Constants;
using Ecliptix.Utilities;
using Grpc.Core;

namespace Ecliptix.Network.Services.Network.Resilience;

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
        {
            return false;
        }

        string detail = ex.Status.Detail ?? string.Empty;

        return detail.Contains("header authentication failed", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("requested index", StringComparison.OrdinalIgnoreCase) && detail.Contains("not future", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("message index too far", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("message index too old", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("message index already processed", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("nonce/index", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("index binding failed", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("chain index", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("chain rotation", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("sequence mismatch", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("protocol state", StringComparison.OrdinalIgnoreCase) && detail.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("protocol state", StringComparison.OrdinalIgnoreCase) && detail.Contains("desynchronized", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("dhpublic", StringComparison.OrdinalIgnoreCase) && detail.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("sender chain", StringComparison.OrdinalIgnoreCase) && detail.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("receiver chain", StringComparison.OrdinalIgnoreCase) && detail.Contains("invalid", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("protocol version", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("state version", StringComparison.OrdinalIgnoreCase) ||
               detail.Contains("channel state", StringComparison.OrdinalIgnoreCase) && detail.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsServerShutdown(RpcException ex) =>
        ex.StatusCode == StatusCode.Unavailable &&
        (ex.Status.Detail?.Contains("shutdown", StringComparison.OrdinalIgnoreCase) is true ||
         ex.Status.Detail?.Contains("maintenance", StringComparison.OrdinalIgnoreCase) is true);

    public static bool IsCancelled(RpcException ex) =>
        ex.StatusCode == StatusCode.Cancelled;

    public static bool IsIdentityKeyDerivationFailure(RpcException ex) =>
        ex.StatusCode == StatusCode.Unauthenticated &&
        (ex.Status.Detail?.Contains("IDENTITY_KEY_DERIVATION_FAILED", System.StringComparison.Ordinal) is true);

    public static bool IsMasterKeySharesNotFound(RpcException ex) =>
        ex.StatusCode == StatusCode.Internal &&
        (ex.Status.Detail?.Contains("master_key_shares_not_found", StringComparison.OrdinalIgnoreCase) is true);

    public static bool IsMasterKeyMismatch(RpcException ex) =>
        ex.StatusCode == StatusCode.FailedPrecondition &&
        (ex.Status.Detail?.Contains("master_key_mismatch", StringComparison.OrdinalIgnoreCase) is true);

    public static bool IsAuthFlowMissing(RpcException ex)
    {
        if (ex.StatusCode != StatusCode.NotFound)
        {
            return false;
        }

        return GetMetadataValue(ex.Trailers, GrpcErrorMetadataKeys.ERROR_CODE)
                   .Where(code => string.Equals(code, "AuthFlowMissing", StringComparison.OrdinalIgnoreCase))
                   .IsSome
               ||
               GetMetadataValue(ex.Trailers, GrpcErrorMetadataKeys.I_18_N_KEY)
                   .Where(key => string.Equals(key, "error.auth_flow_missing", StringComparison.OrdinalIgnoreCase))
                   .IsSome;
    }

    private static Option<string> GetMetadataValue(Metadata metadata, string key)
    {
        Metadata.Entry? entry = metadata
            .FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        return entry?.Value.ToOption() ?? Option<string>.None;
    }
}
