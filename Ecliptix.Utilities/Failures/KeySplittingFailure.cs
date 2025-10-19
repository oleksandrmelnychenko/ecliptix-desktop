using System;
using Grpc.Core;

namespace Ecliptix.Utilities.Failures;

public sealed record KeySplittingFailure : FailureBase
{
    public enum ErrorCode
    {
        InsufficientShares,
        ShareValidationFailed,
        ShareStorageFailed,
        ShareRetrievalFailed,
        ShareNotFound,
        CacheCapacityExceeded,
        InvalidShareData,
        InvalidIdentifier,
        HmacKeyMissing,
        HmacKeyGenerationFailed,
        HmacKeyStorageFailed,
        HmacKeyRetrievalFailed,
        HmacKeyRemovalFailed,
        StorageDisposed,
        AllocationFailed,
        MemoryWriteFailed,
        MemoryReadFailed,
        EncryptionFailed,
        DecryptionFailed,
        InvalidThreshold,
        InvalidShareCount,
        KeyDerivationFailed,
        KeyReconstructionFailed,
        KeySplittingFailed,
        HardwareSecurityUnavailable,
        MinimumSharesNotMet,
        InvalidKeyLength,
        InvalidKeyData,
        InvalidDataFormat,
        KeyNotFoundInKeychain
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

    public static KeySplittingFailure InsufficientShares(int retrieved, int required) =>
        new(ErrorCode.InsufficientShares, $"Insufficient shares retrieved. Got {retrieved}, need {required}");

    public static KeySplittingFailure ShareValidationFailed(string reason) =>
        new(ErrorCode.ShareValidationFailed, $"Share validation failed: {reason}");

    public static KeySplittingFailure ShareStorageFailed(int shareIndex, string reason) =>
        new(ErrorCode.ShareStorageFailed, $"Failed to store share {shareIndex}: {reason}");

    public static KeySplittingFailure ShareRetrievalFailed(int shareIndex, string reason) =>
        new(ErrorCode.ShareRetrievalFailed, $"Failed to retrieve share {shareIndex}: {reason}");

    public static KeySplittingFailure ShareNotFound(int shareIndex) =>
        new(ErrorCode.ShareNotFound, $"Share {shareIndex} not found");

    public static KeySplittingFailure CacheCapacityExceeded(int currentCount, int limit) =>
        new(ErrorCode.CacheCapacityExceeded, $"Cache capacity exceeded: {currentCount}/{limit}");

    public static KeySplittingFailure InvalidShareData(string reason) =>
        new(ErrorCode.InvalidShareData, $"Invalid share data: {reason}");

    public static KeySplittingFailure InvalidIdentifier(string identifier) =>
        new(ErrorCode.InvalidIdentifier, $"Invalid identifier: {identifier}");

    public static KeySplittingFailure HmacKeyMissing(string identifier) =>
        new(ErrorCode.HmacKeyMissing, $"HMAC key not found for identifier: {identifier}");

    public static KeySplittingFailure HmacKeyGenerationFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.HmacKeyGenerationFailed, $"Failed to generate HMAC key: {reason}", ex);

    public static KeySplittingFailure HmacKeyStorageFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.HmacKeyStorageFailed, $"Failed to store HMAC key: {reason}", ex);

    public static KeySplittingFailure HmacKeyRetrievalFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.HmacKeyRetrievalFailed, $"Failed to retrieve HMAC key: {reason}", ex);

    public static KeySplittingFailure StorageDisposed() =>
        new(ErrorCode.StorageDisposed, "Storage service is disposed");

    public static KeySplittingFailure AllocationFailed(string reason) =>
        new(ErrorCode.AllocationFailed, $"Failed to allocate secure memory: {reason}");

    public static KeySplittingFailure MemoryWriteFailed(string reason) =>
        new(ErrorCode.MemoryWriteFailed, $"Failed to write to secure memory: {reason}");

    public static KeySplittingFailure MemoryReadFailed(string reason) =>
        new(ErrorCode.MemoryReadFailed, $"Failed to read from secure memory: {reason}");

    public static KeySplittingFailure EncryptionFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.EncryptionFailed, $"Encryption failed: {reason}", ex);

    public static KeySplittingFailure DecryptionFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.DecryptionFailed, $"Decryption failed: {reason}", ex);

    public static KeySplittingFailure InvalidThreshold(int threshold, int totalShares) =>
        new(ErrorCode.InvalidThreshold, $"Invalid threshold: {threshold}. Must be between 2 and {totalShares}");

    public static KeySplittingFailure InvalidShareCount(int count) =>
        new(ErrorCode.InvalidShareCount, $"Invalid share count: {count}. Must be between 2 and 255");

    public static KeySplittingFailure KeyDerivationFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.KeyDerivationFailed, $"Key derivation failed: {reason}", ex);

    public static KeySplittingFailure KeyReconstructionFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.KeyReconstructionFailed, $"Key reconstruction failed: {reason}", ex);

    public static KeySplittingFailure KeySplittingFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.KeySplittingFailed, $"Key splitting failed: {reason}", ex);

    public static KeySplittingFailure HardwareSecurityUnavailable(string reason) =>
        new(ErrorCode.HardwareSecurityUnavailable, $"Hardware security unavailable: {reason}");

    public static KeySplittingFailure MinimumSharesNotMet(int successCount, int required) =>
        new(ErrorCode.MinimumSharesNotMet, $"Failed to store minimum required shares. Only {successCount} of {required} succeeded");

    public static KeySplittingFailure InvalidKeyLength(int length) =>
        new(ErrorCode.InvalidKeyLength, $"Invalid key length: {length}");

    public static KeySplittingFailure InvalidKeyData(string reason) =>
        new(ErrorCode.InvalidKeyData, $"Invalid key data: {reason}");

    public static KeySplittingFailure HmacKeyRemovalFailed(string reason, Exception? ex = null) =>
        new(ErrorCode.HmacKeyRemovalFailed, $"Failed to remove HMAC key: {reason}", ex);

    public static KeySplittingFailure InvalidDataFormat(string reason) =>
        new(ErrorCode.InvalidDataFormat, $"Invalid data format: {reason}");

    public static KeySplittingFailure KeyNotFoundInKeychain(string keyIdentifier) =>
        new(ErrorCode.KeyNotFoundInKeychain, $"Key not found in keychain: {keyIdentifier}");

    public static KeySplittingFailure FromSodiumFailure(Sodium.SodiumFailure failure) =>
        new(ErrorCode.AllocationFailed, $"Sodium operation failed: {failure.Message}", failure.InnerException);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(Utilities.ErrorCode.InternalError, StatusCode.Internal, ErrorI18nKeys.Internal);

    public override string ToString() => $"[KeySplittingFailure.{Code}] {Message}";
}
