using Grpc.Core;

namespace Ecliptix.Utilities;

public sealed record GrpcErrorDescriptor(
    ErrorCode ERROR_CODE,
    StatusCode StatusCode,
    string I_18N_KEY,
    bool RETRYABLE = false,
    int? RETRY_AFTER_MILLISECONDS = null)
{
    public Status CreateStatus(string message) => new(StatusCode, message);
}
