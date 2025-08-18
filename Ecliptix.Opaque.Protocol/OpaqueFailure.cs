namespace Ecliptix.Opaque.Protocol;

public sealed record OpaqueFailure
{
    public OpaqueCryptoFailureType Type { get; }
    public string Message { get; }
    public Exception? InnerException { get; }

    private OpaqueFailure(OpaqueCryptoFailureType type, string message, Exception? innerException = null)
    {
        Type = type;
        Message = message;
        InnerException = innerException;
    }

    public static OpaqueFailure MacVerificationFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.MacVerificationFailed,
            string.IsNullOrEmpty(details) ? OpaqueMessageKeys.MacVerificationFailed : details, inner);
    }

    public static OpaqueFailure InvalidKeySignature(string details, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature, details, inner);
    }

    public static OpaqueFailure HashingValidPointFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.HashingValidPointFailed,
            string.IsNullOrEmpty(details) ? OpaqueMessageKeys.HashingValidPointFailed : details, inner);
    }

    public static OpaqueFailure DecryptFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.DecryptFailure,
            string.IsNullOrEmpty(details) ? OpaqueMessageKeys.DecryptFailed : details, inner);
    }

    public static OpaqueFailure EncryptFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.EncryptFailure,
            string.IsNullOrEmpty(details) ? OpaqueMessageKeys.EncryptFailed : details, inner);
    }

    public static OpaqueFailure InvalidInput(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidInput,
            string.IsNullOrEmpty(details) ? OpaqueMessageKeys.InputKeyingMaterialCannotBeNullOrEmpty : details, inner);
    }

    public static OpaqueFailure InvalidPoint(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? "Invalid elliptic curve point" : details, inner);
    }

    public static OpaqueFailure SubgroupCheckFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? "Point not in main subgroup" : details, inner);
    }

    public static OpaqueFailure StretchingFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidInput,
            string.IsNullOrEmpty(details) ? "PBKDF2 stretching failed" : details, inner);
    }

    public static OpaqueFailure EnvelopeFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? "Envelope operation failed" : details, inner);
    }

    public static OpaqueFailure MaskingFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidInput,
            string.IsNullOrEmpty(details) ? "Response masking/unmasking failed" : details, inner);
    }

    public static OpaqueFailure KeyDerivationFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? "Key derivation failed" : details, inner);
    }
}