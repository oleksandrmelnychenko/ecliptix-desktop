using Ecliptix.Utilities;
using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Network;

public sealed record UserFacingError(
    ErrorCode ErrorCode,
    string I18nKey,
    string Message,
    bool? Retryable = null,
    int? RetryAfterMilliseconds = null,
    string? CorrelationId = null,
    string? Locale = null,
    StatusCode? GrpcStatusCode = null);
