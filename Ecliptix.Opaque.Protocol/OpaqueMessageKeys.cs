namespace Ecliptix.Opaque.Protocol;

public static class OpaqueMessageKeys
{
    public const string InputKeyingMaterialCannotBeNullOrEmpty = "Opaque input keying material (ikm) cannot be null or empty";
    public const string HashingValidPointFailed = "Opaque Failed to hash input to a valid curve point after 255 attempts.";
    public const string DecryptFailed = "Opaque decryption failed";
    public const string EncryptFailed = "Opaque encryption failed";
    public const string MacVerificationFailed = "Opaque MAC verification failed";
}