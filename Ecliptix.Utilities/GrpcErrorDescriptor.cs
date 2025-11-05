using Grpc.Core;

namespace Ecliptix.Utilities;

public sealed record GrpcErrorDescriptor(
    ErrorCode ErrorCode,
    StatusCode StatusCode,
    string I18NKey,
    bool Retryable = false,
    int? RetryAfterMilliseconds = null)
{
    public Status CreateStatus(string message) => new(StatusCode, message);
}
