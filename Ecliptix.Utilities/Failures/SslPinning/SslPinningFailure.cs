namespace Ecliptix.Utilities.Failures.SslPinning;

public record SslPinningFailure(
    SslPinningFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog()
    {
        return new
        {
            SslPinningFailureType = FailureType.ToString(),
            Message,
            InnerException,
            Timestamp
        };
    }

    public static SslPinningFailure ServiceNotInitialized() =>
        new(SslPinningFailureType.ServiceNotInitialized, SslPinningFailureMessages.ServiceNotInitialized);

    public static SslPinningFailure ServiceDisposed() =>
        new(SslPinningFailureType.ServiceDisposed, SslPinningFailureMessages.ServiceDisposed);

    public static SslPinningFailure LibraryInitializationFailed(string details) =>
        new(SslPinningFailureType.LibraryInitializationFailed, $"{SslPinningFailureMessages.LibraryInitializationFailed}: {details}");

    public static SslPinningFailure InitializationException(Exception ex) =>
        new(SslPinningFailureType.InitializationException, $"{SslPinningFailureMessages.InitializationException}: {ex.Message}", ex);

    public static SslPinningFailure CertificateDataRequired() =>
        new(SslPinningFailureType.CertificateDataRequired, SslPinningFailureMessages.CertificateDataRequired);

    public static SslPinningFailure HostnameRequired() =>
        new(SslPinningFailureType.HostnameRequired, SslPinningFailureMessages.HostnameRequired);

    public static SslPinningFailure CertificateValidationFailed(string details) =>
        new(SslPinningFailureType.CertificateValidationFailed, $"{SslPinningFailureMessages.CertificateValidationFailed}: {details}");

    public static SslPinningFailure CertificateValidationException(Exception ex) =>
        new(SslPinningFailureType.CertificateValidationException, $"{SslPinningFailureMessages.CertificateValidationException}: {ex.Message}", ex);

    public static SslPinningFailure PlaintextRequired() =>
        new(SslPinningFailureType.PlaintextRequired, SslPinningFailureMessages.PlaintextRequired);

    public static SslPinningFailure PlaintextTooLarge() =>
        new(SslPinningFailureType.PlaintextTooLarge, SslPinningFailureMessages.PlaintextTooLarge);

    public static SslPinningFailure RsaEncryptionFailed(string details) =>
        new(SslPinningFailureType.RsaEncryptionFailed, $"{SslPinningFailureMessages.RsaEncryptionFailed}: {details}");

    public static SslPinningFailure RsaEncryptionException(Exception ex) =>
        new(SslPinningFailureType.RsaEncryptionException, $"{SslPinningFailureMessages.RsaEncryptionException}: {ex.Message}", ex);

    public static SslPinningFailure CiphertextRequired() =>
        new(SslPinningFailureType.CiphertextRequired, SslPinningFailureMessages.CiphertextRequired);

    public static SslPinningFailure PrivateKeyRequired() =>
        new(SslPinningFailureType.PrivateKeyRequired, SslPinningFailureMessages.PrivateKeyRequired);

    public static SslPinningFailure RsaDecryptionFailed(string details) =>
        new(SslPinningFailureType.RsaDecryptionFailed, $"{SslPinningFailureMessages.RsaDecryptionFailed}: {details}");

    public static SslPinningFailure RsaDecryptionException(Exception ex) =>
        new(SslPinningFailureType.RsaDecryptionException, $"{SslPinningFailureMessages.RsaDecryptionException}: {ex.Message}", ex);

    public static SslPinningFailure InvalidCiphertextSize() =>
        new(SslPinningFailureType.InvalidCiphertextSize, SslPinningFailureMessages.InvalidCiphertextSize);

    public static SslPinningFailure InvalidKeySize(int expectedSize) =>
        new(SslPinningFailureType.InvalidKeySize, $"Key must be {expectedSize} bytes");

    public static SslPinningFailure AesGcmDecryptionFailed(string details) =>
        new(SslPinningFailureType.AesGcmDecryptionFailed, $"{SslPinningFailureMessages.AesGcmDecryptionFailed}: {details}");

    public static SslPinningFailure AesGcmDecryptionException(Exception ex) =>
        new(SslPinningFailureType.AesGcmDecryptionException, $"{SslPinningFailureMessages.AesGcmDecryptionException}: {ex.Message}", ex);

    public static SslPinningFailure MessageRequired() =>
        new(SslPinningFailureType.MessageRequired, SslPinningFailureMessages.MessageRequired);

    public static SslPinningFailure InvalidPrivateKeySize(int expectedSize) =>
        new(SslPinningFailureType.InvalidPrivateKeySize, $"Private key must be {expectedSize} bytes");

    public static SslPinningFailure Ed25519SigningFailed(string details) =>
        new(SslPinningFailureType.Ed25519SigningFailed, $"{SslPinningFailureMessages.Ed25519SigningFailed}: {details}");

    public static SslPinningFailure Ed25519SigningException(Exception ex) =>
        new(SslPinningFailureType.Ed25519SigningException, $"{SslPinningFailureMessages.Ed25519SigningException}: {ex.Message}", ex);

    public static SslPinningFailure InvalidSignatureSize(int expectedSize) =>
        new(SslPinningFailureType.InvalidSignatureSize, $"Signature must be {expectedSize} bytes");

    public static SslPinningFailure Ed25519VerificationError(string details) =>
        new(SslPinningFailureType.Ed25519VerificationError, $"{SslPinningFailureMessages.Ed25519VerificationError}: {details}");

    public static SslPinningFailure Ed25519VerificationException(Exception ex) =>
        new(SslPinningFailureType.Ed25519VerificationException, $"{SslPinningFailureMessages.Ed25519VerificationException}: {ex.Message}", ex);

    public static SslPinningFailure InvalidNonceSize() =>
        new(SslPinningFailureType.InvalidNonceSize, SslPinningFailureMessages.InvalidNonceSize);

    public static SslPinningFailure RandomBytesGenerationFailed(string details) =>
        new(SslPinningFailureType.RandomBytesGenerationFailed, $"{SslPinningFailureMessages.RandomBytesGenerationFailed}: {details}");

    public static SslPinningFailure RandomBytesGenerationException(Exception ex) =>
        new(SslPinningFailureType.RandomBytesGenerationException, $"{SslPinningFailureMessages.RandomBytesGenerationException}: {ex.Message}", ex);

    public static SslPinningFailure LibraryCleanupError(Exception ex) =>
        new(SslPinningFailureType.LibraryCleanupError, $"{SslPinningFailureMessages.LibraryCleanupError}: {ex.Message}", ex);

    public static SslPinningFailure SecureMemoryAllocationFailed(string details) =>
        new(SslPinningFailureType.SecureMemoryAllocationFailed, $"{SslPinningFailureMessages.SecureMemoryAllocationFailed}: {details}");

    public static SslPinningFailure SecureMemoryWriteFailed(string details) =>
        new(SslPinningFailureType.SecureMemoryWriteFailed, $"{SslPinningFailureMessages.SecureMemoryWriteFailed}: {details}");

    public static SslPinningFailure SecureMemoryReadFailed(string details) =>
        new(SslPinningFailureType.SecureMemoryReadFailed, $"{SslPinningFailureMessages.SecureMemoryReadFailed}: {details}");
}