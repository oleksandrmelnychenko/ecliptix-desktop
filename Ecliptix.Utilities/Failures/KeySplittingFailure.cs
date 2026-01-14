using Grpc.Core;

namespace Ecliptix.Utilities.Failures;

public sealed record KeySplittingFailure : FailureBase
{
    public enum ErrorCode
    {
        ALLOCATION_FAILED,
        MEMORY_WRITE_FAILED,
        MEMORY_READ_FAILED,
        KEY_DERIVATION_FAILED,
    }

    public ErrorCode Code { get; }

    private KeySplittingFailure(ErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public override object ToStructuredLog() => new
    {
        ErrorCode = Code.ToString(),
        Message,
        InnerException = InnerException?.Message,
        Timestamp
    };

    public static KeySplittingFailure AllocationFailed(string reason) =>
        new(ErrorCode.ALLOCATION_FAILED, $"Failed to allocate secure memory: {reason}");

    public static KeySplittingFailure MemoryWriteFailed(string reason) =>
        new(ErrorCode.MEMORY_WRITE_FAILED, $"Failed to write to secure memory: {reason}");

    public static KeySplittingFailure MemoryReadFailed(string reason) =>
        new(ErrorCode.MEMORY_READ_FAILED, $"Failed to read from secure memory: {reason}");

    public static KeySplittingFailure KeyDerivationFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.KEY_DERIVATION_FAILED, $"Key derivation failed: {reason}", ex);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(Utilities.ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL);

    public override string ToString() => $"[KeySplittingFailure.{Code}] {Message}";
}
