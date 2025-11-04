using System;
using Grpc.Core;

namespace Ecliptix.Utilities.Failures;

public sealed record KeySplittingFailure : FailureBase
{
    public enum ERROR_CODE
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
        STORAGE_DISPOSED,
        ALLOCATION_FAILED,
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

    public ERROR_CODE Code { get; }

    private KeySplittingFailure(ERROR_CODE code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public override object ToStructuredLog() => new
    {
        ERROR_CODE = Code.ToString(),
        Message,
        InnerException = InnerException?.Message,
        Timestamp
    };

    public static KeySplittingFailure InsufficientShares(int retrieved, int required) =>
        new(ERROR_CODE.InsufficientShares, $"Insufficient shares retrieved. Got {retrieved}, need {required}");

    public static KeySplittingFailure ShareValidationFailed(string reason) =>
        new(ERROR_CODE.ShareValidationFailed, $"Share validation failed: {reason}");

    public static KeySplittingFailure ShareStorageFailed(int shareIndex, string reason) =>
        new(ERROR_CODE.ShareStorageFailed, $"Failed to store share {shareIndex}: {reason}");

    public static KeySplittingFailure ShareRetrievalFailed(int shareIndex, string reason) =>
        new(ERROR_CODE.ShareRetrievalFailed, $"Failed to retrieve share {shareIndex}: {reason}");

    public static KeySplittingFailure ShareNotFound(int shareIndex) =>
        new(ERROR_CODE.ShareNotFound, $"Share {shareIndex} not found");

    public static KeySplittingFailure CacheCapacityExceeded(int currentCount, int limit) =>
        new(ERROR_CODE.CacheCapacityExceeded, $"Cache capacity exceeded: {currentCount}/{limit}");

    public static KeySplittingFailure InvalidShareData(string reason) =>
        new(ERROR_CODE.InvalidShareData, $"Invalid share data: {reason}");

    public static KeySplittingFailure InvalidIdentifier(string identifier) =>
        new(ERROR_CODE.InvalidIdentifier, $"Invalid identifier: {identifier}");

    public static KeySplittingFailure HmacKeyMissing(string identifier) =>
        new(ERROR_CODE.HmacKeyMissing, $"HMAC key not found for identifier: {identifier}");

    public static KeySplittingFailure HmacKeyGenerationFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.HmacKeyGenerationFailed, $"Failed to generate HMAC key: {reason}", ex);

    public static KeySplittingFailure HmacKeyStorageFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.HmacKeyStorageFailed, $"Failed to store HMAC key: {reason}", ex);

    public static KeySplittingFailure HmacKeyRetrievalFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.HmacKeyRetrievalFailed, $"Failed to retrieve HMAC key: {reason}", ex);

    public static KeySplittingFailure STORAGE_DISPOSED() =>
        new(ERROR_CODE.STORAGE_DISPOSED, "Storage service is disposed");

    public static KeySplittingFailure ALLOCATION_FAILED(string reason) =>
        new(ERROR_CODE.ALLOCATION_FAILED, $"Failed to allocate secure memory: {reason}");

    public static KeySplittingFailure MemoryWriteFailed(string reason) =>
        new(ERROR_CODE.MemoryWriteFailed, $"Failed to write to secure memory: {reason}");

    public static KeySplittingFailure MemoryReadFailed(string reason) =>
        new(ERROR_CODE.MemoryReadFailed, $"Failed to read from secure memory: {reason}");

    public static KeySplittingFailure EncryptionFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.EncryptionFailed, $"Encryption failed: {reason}", ex);

    public static KeySplittingFailure DecryptionFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.DecryptionFailed, $"Decryption failed: {reason}", ex);

    public static KeySplittingFailure InvalidThreshold(int threshold, int totalShares) =>
        new(ERROR_CODE.InvalidThreshold, $"Invalid threshold: {threshold}. Must be between 2 and {totalShares}");

    public static KeySplittingFailure InvalidShareCount(int count) =>
        new(ERROR_CODE.InvalidShareCount, $"Invalid share count: {count}. Must be between 2 and 255");

    public static KeySplittingFailure KeyDerivationFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.KeyDerivationFailed, $"Key derivation failed: {reason}", ex);

    public static KeySplittingFailure KeyReconstructionFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.KeyReconstructionFailed, $"Key reconstruction failed: {reason}", ex);

    public static KeySplittingFailure KeySplittingFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.KeySplittingFailed, $"Key splitting failed: {reason}", ex);

    public static KeySplittingFailure HardwareSecurityUnavailable(string reason) =>
        new(ERROR_CODE.HardwareSecurityUnavailable, $"Hardware security unavailable: {reason}");

    public static KeySplittingFailure MinimumSharesNotMet(int successCount, int required) =>
        new(ERROR_CODE.MinimumSharesNotMet, $"Failed to store minimum required shares. Only {successCount} of {required} succeeded");

    public static KeySplittingFailure InvalidKeyLength(int length) =>
        new(ERROR_CODE.InvalidKeyLength, $"Invalid key length: {length}");

    public static KeySplittingFailure InvalidKeyData(string reason) =>
        new(ERROR_CODE.InvalidKeyData, $"Invalid key data: {reason}");

    public static KeySplittingFailure HmacKeyRemovalFailed(string reason, Exception? ex = null) =>
        new(ERROR_CODE.HmacKeyRemovalFailed, $"Failed to remove HMAC key: {reason}", ex);

    public static KeySplittingFailure InvalidDataFormat(string reason) =>
        new(ERROR_CODE.InvalidDataFormat, $"Invalid data format: {reason}");

    public static KeySplittingFailure KeyNotFoundInKeychain(string keyIdentifier) =>
        new(ERROR_CODE.KeyNotFoundInKeychain, $"Key not found in keychain: {keyIdentifier}");

    public static KeySplittingFailure FromSodiumFailure(Sodium.SodiumFailure failure) =>
        new(ERROR_CODE.ALLOCATION_FAILED, $"Sodium operation failed: {failure.Message}", failure.InnerException);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(Utilities.ERROR_CODE.InternalError, StatusCode.Internal, ErrorI18nKeys.INTERNAL);

    public override string ToString() => $"[KeySplittingFailure.{Code}] {Message}";
}
