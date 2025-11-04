using Grpc.Core;

namespace Ecliptix.Utilities.Failures;

public sealed record KeySplittingFailure : FailureBase
{
    public enum ErrorCode
    {
        INSUFFICIENT_SHARES,
        SHARE_VALIDATION_FAILED,
        SHARE_STORAGE_FAILED,
        SHARE_RETRIEVAL_FAILED,
        SHARE_NOT_FOUND,
        CACHE_CAPACITY_EXCEEDED,
        INVALID_SHARE_DATA,
        INVALID_IDENTIFIER,
        HMAC_KEY_MISSING,
        HMAC_KEY_GENERATION_FAILED,
        HMAC_KEY_STORAGE_FAILED,
        HMAC_KEY_RETRIEVAL_FAILED,
        HMAC_KEY_REMOVAL_FAILED,
        STORAGE_DISPOSED,
        ALLOCATION_FAILED,
        MEMORY_WRITE_FAILED,
        MEMORY_READ_FAILED,
        ENCRYPTION_FAILED,
        DECRYPTION_FAILED,
        INVALID_THRESHOLD,
        INVALID_SHARE_COUNT,
        KEY_DERIVATION_FAILED,
        KEY_RECONSTRUCTION_FAILED,
        KEY_SPLITTING_FAILED,
        HARDWARE_SECURITY_UNAVAILABLE,
        MINIMUM_SHARES_NOT_MET,
        INVALID_KEY_LENGTH,
        INVALID_KEY_DATA,
        INVALID_DATA_FORMAT,
        KEY_NOT_FOUND_IN_KEYCHAIN
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

    public static KeySplittingFailure ALLOCATION_FAILED(string reason) =>
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
