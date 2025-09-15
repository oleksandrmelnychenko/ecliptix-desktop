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

    public static OpaqueFailure InvalidInput(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidInput,
            string.IsNullOrEmpty(details) ? OpaqueMessageKeys.InputKeyingMaterialCannotBeNullOrEmpty : details, inner);
    }

    public static OpaqueFailure InvalidPoint(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? OpaqueConstants.ErrorMessages.InvalidEllipticCurvePoint : details, inner);
    }

    public static OpaqueFailure SubgroupCheckFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? OpaqueConstants.ErrorMessages.PointNotInMainSubgroup : details, inner);
    }

    public static OpaqueFailure StretchingFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidInput,
            string.IsNullOrEmpty(details) ? OpaqueConstants.ErrorMessages.Pbkdf2StretchingFailed : details, inner);
    }

    public static OpaqueFailure EnvelopeFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? OpaqueConstants.ErrorMessages.EnvelopeOperationFailed : details, inner);
    }

    public static OpaqueFailure MaskingFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidInput,
            string.IsNullOrEmpty(details) ? OpaqueConstants.ErrorMessages.ResponseMaskingFailed : details, inner);
    }

    public static OpaqueFailure KeyDerivationFailed(string? details = null, Exception? inner = null)
    {
        return new OpaqueFailure(OpaqueCryptoFailureType.InvalidKeySignature,
            string.IsNullOrEmpty(details) ? OpaqueConstants.ErrorMessages.KeyDerivationFailed : details, inner);
    }
}

public class OpaqueAuthenticationException(string message) : Exception(message);