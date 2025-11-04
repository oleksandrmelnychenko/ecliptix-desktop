using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Network;

public sealed record UserFacingError(
    ErrorCode ERROR_CODE,
    string I_18N_KEY,
    string Message,
    bool? RETRYABLE = null,
    int? RETRY_AFTER_MILLISECONDS = null,
    string? CORRELATION_ID = null,
    string? LOCALE = null,
    StatusCode? GrpcStatusCode = null);
