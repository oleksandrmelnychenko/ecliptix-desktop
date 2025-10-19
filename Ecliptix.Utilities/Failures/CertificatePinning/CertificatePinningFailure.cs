using Grpc.Core;

namespace Ecliptix.Utilities.Failures.CertificatePinning;

public record CertificatePinningFailure(
    CertificatePinningFailureType FailureType,
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

    public static CertificatePinningFailure ServiceNotInitialized() =>
        new(CertificatePinningFailureType.ServiceNotInitialized, CertificatePinningFailureMessages.ServiceNotInitialized);

    public static CertificatePinningFailure ServiceDisposed() =>
        new(CertificatePinningFailureType.ServiceDisposed, CertificatePinningFailureMessages.ServiceDisposed);

    public static CertificatePinningFailure LibraryInitializationFailed(string details) =>
        new(CertificatePinningFailureType.LibraryInitializationFailed, $"{CertificatePinningFailureMessages.LibraryInitializationFailed}: {details}");

    public static CertificatePinningFailure InitializationException(Exception ex) =>
        new(CertificatePinningFailureType.InitializationException, $"{CertificatePinningFailureMessages.InitializationException}: {ex.Message}", ex);

    public static CertificatePinningFailure CertificateDataRequired() =>
        new(CertificatePinningFailureType.CertificateDataRequired, CertificatePinningFailureMessages.CertificateDataRequired);

    public static CertificatePinningFailure HostnameRequired() =>
        new(CertificatePinningFailureType.HostnameRequired, CertificatePinningFailureMessages.HostnameRequired);

    public static CertificatePinningFailure CertificateValidationFailed(string details) =>
        new(CertificatePinningFailureType.CertificateValidationFailed, $"{CertificatePinningFailureMessages.CertificateValidationFailed}: {details}");

    public static CertificatePinningFailure CertificateValidationException(Exception ex) =>
        new(CertificatePinningFailureType.CertificateValidationException, $"{CertificatePinningFailureMessages.CertificateValidationException}: {ex.Message}", ex);

    public static CertificatePinningFailure PlaintextRequired() =>
        new(CertificatePinningFailureType.PlaintextRequired, CertificatePinningFailureMessages.PlaintextRequired);

    public static CertificatePinningFailure PlaintextTooLarge() =>
        new(CertificatePinningFailureType.PlaintextTooLarge, CertificatePinningFailureMessages.PlaintextTooLarge);

    public static CertificatePinningFailure RsaEncryptionFailed(string details) =>
        new(CertificatePinningFailureType.RsaEncryptionFailed, $"{CertificatePinningFailureMessages.RsaEncryptionFailed}: {details}");

    public static CertificatePinningFailure RsaEncryptionException(Exception ex) =>
        new(CertificatePinningFailureType.RsaEncryptionException, $"{CertificatePinningFailureMessages.RsaEncryptionException}: {ex.Message}", ex);

    public static CertificatePinningFailure CiphertextRequired() =>
        new(CertificatePinningFailureType.CiphertextRequired, CertificatePinningFailureMessages.CiphertextRequired);

    public static CertificatePinningFailure PrivateKeyRequired() =>
        new(CertificatePinningFailureType.PrivateKeyRequired, CertificatePinningFailureMessages.PrivateKeyRequired);

    public static CertificatePinningFailure RsaDecryptionFailed(string details) =>
        new(CertificatePinningFailureType.RsaDecryptionFailed, $"{CertificatePinningFailureMessages.RsaDecryptionFailed}: {details}");

    public static CertificatePinningFailure RsaDecryptionException(Exception ex) =>
        new(CertificatePinningFailureType.RsaDecryptionException, $"{CertificatePinningFailureMessages.RsaDecryptionException}: {ex.Message}", ex);

    public static CertificatePinningFailure InvalidCiphertextSize() =>
        new(CertificatePinningFailureType.InvalidCiphertextSize, CertificatePinningFailureMessages.InvalidCiphertextSize);

    public static CertificatePinningFailure InvalidKeySize(int expectedSize) =>
        new(CertificatePinningFailureType.InvalidKeySize, $"Key must be {expectedSize} bytes");

    public static CertificatePinningFailure AesGcmDecryptionFailed(string details) =>
        new(CertificatePinningFailureType.AesGcmDecryptionFailed, $"{CertificatePinningFailureMessages.AesGcmDecryptionFailed}: {details}");

    public static CertificatePinningFailure AesGcmDecryptionException(Exception ex) =>
        new(CertificatePinningFailureType.AesGcmDecryptionException, $"{CertificatePinningFailureMessages.AesGcmDecryptionException}: {ex.Message}", ex);

    public static CertificatePinningFailure MessageRequired() =>
        new(CertificatePinningFailureType.MessageRequired, CertificatePinningFailureMessages.MessageRequired);

    public static CertificatePinningFailure InvalidPrivateKeySize(int expectedSize) =>
        new(CertificatePinningFailureType.InvalidPrivateKeySize, $"Private key must be {expectedSize} bytes");

    public static CertificatePinningFailure Ed25519SigningFailed(string details) =>
        new(CertificatePinningFailureType.Ed25519SigningFailed, $"{CertificatePinningFailureMessages.Ed25519SigningFailed}: {details}");

    public static CertificatePinningFailure Ed25519SigningException(Exception ex) =>
        new(CertificatePinningFailureType.Ed25519SigningException, $"{CertificatePinningFailureMessages.Ed25519SigningException}: {ex.Message}", ex);

    public static CertificatePinningFailure InvalidSignatureSize(int expectedSize) =>
        new(CertificatePinningFailureType.InvalidSignatureSize, $"Signature must be {expectedSize} bytes");

    public static CertificatePinningFailure Ed25519VerificationError(string details) =>
        new(CertificatePinningFailureType.Ed25519VerificationError, $"{CertificatePinningFailureMessages.Ed25519VerificationError}: {details}");

    public static CertificatePinningFailure Ed25519VerificationException(Exception ex) =>
        new(CertificatePinningFailureType.Ed25519VerificationException, $"{CertificatePinningFailureMessages.Ed25519VerificationException}: {ex.Message}", ex);

    public static CertificatePinningFailure InvalidNonceSize() =>
        new(CertificatePinningFailureType.InvalidNonceSize, CertificatePinningFailureMessages.InvalidNonceSize);

    public static CertificatePinningFailure RandomBytesGenerationFailed(string details) =>
        new(CertificatePinningFailureType.RandomBytesGenerationFailed, $"{CertificatePinningFailureMessages.RandomBytesGenerationFailed}: {details}");

    public static CertificatePinningFailure RandomBytesGenerationException(Exception ex) =>
        new(CertificatePinningFailureType.RandomBytesGenerationException, $"{CertificatePinningFailureMessages.RandomBytesGenerationException}: {ex.Message}", ex);

    public static CertificatePinningFailure LibraryCleanupError(Exception ex) =>
        new(CertificatePinningFailureType.LibraryCleanupError, $"{CertificatePinningFailureMessages.LibraryCleanupError}: {ex.Message}", ex);

    public static CertificatePinningFailure SecureMemoryAllocationFailed(string details) =>
        new(CertificatePinningFailureType.SecureMemoryAllocationFailed, $"{CertificatePinningFailureMessages.SecureMemoryAllocationFailed}: {details}");

    public static CertificatePinningFailure SecureMemoryWriteFailed(string details) =>
        new(CertificatePinningFailureType.SecureMemoryWriteFailed, $"{CertificatePinningFailureMessages.SecureMemoryWriteFailed}: {details}");

    public static CertificatePinningFailure SecureMemoryReadFailed(string details) =>
        new(CertificatePinningFailureType.SecureMemoryReadFailed, $"{CertificatePinningFailureMessages.SecureMemoryReadFailed}: {details}");

    public static CertificatePinningFailure NativeLibraryNotFound(string operationName, Exception ex) =>
        new(CertificatePinningFailureType.NativeLibraryNotFound, $"{CertificatePinningFailureMessages.NativeLibraryNotFound} during {operationName}: {ex.Message}", ex);

    public static CertificatePinningFailure NativeOperationFailed(string operationName, Exception ex) =>
        new(CertificatePinningFailureType.NativeOperationFailed, $"{CertificatePinningFailureMessages.NativeOperationFailed} during {operationName}: {ex.Message}", ex);

    public static CertificatePinningFailure ServiceInitializing() =>
        new(CertificatePinningFailureType.ServiceInitializing, CertificatePinningFailureMessages.ServiceInitializing);

    public static CertificatePinningFailure ServiceInvalidState() =>
        new(CertificatePinningFailureType.ServiceInvalidState, CertificatePinningFailureMessages.ServiceInvalidState);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(ErrorCode.InternalError, StatusCode.Internal, ErrorI18nKeys.Internal);
}
