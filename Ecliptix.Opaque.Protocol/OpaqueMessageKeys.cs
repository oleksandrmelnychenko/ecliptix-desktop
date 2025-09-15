namespace Ecliptix.Opaque.Protocol;

public static class OpaqueMessageKeys
{
    public const string InputKeyingMaterialCannotBeNullOrEmpty =
        OpaqueConstants.ErrorMessages.OpaqueInputKeyingMaterialEmpty;

    public const string HashingValidPointFailed =
        OpaqueConstants.ErrorMessages.OpaqueFailedToHashAfterMaxAttempts;

    public const string DecryptFailed = OpaqueConstants.ErrorMessages.OpaqueDecryptionFailed;

    public const string EncryptFailed = OpaqueConstants.ErrorMessages.OpaqueEncryptionFailed;

    public const string MacVerificationFailed = OpaqueConstants.ErrorMessages.OpaqueMacVerificationFailed;
}