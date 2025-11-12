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
        new(CertificatePinningFailureType.SERVICE_NOT_INITIALIZED, CertificatePinningFailureMessages.SERVICE_NOT_INITIALIZED);

    public static CertificatePinningFailure ServiceDisposed() =>
        new(CertificatePinningFailureType.SERVICE_DISPOSED, CertificatePinningFailureMessages.SERVICE_DISPOSED);

    public static CertificatePinningFailure LibraryInitializationFailed(string details) =>
        new(CertificatePinningFailureType.LIBRARY_INITIALIZATION_FAILED, $"{CertificatePinningFailureMessages.LIBRARY_INITIALIZATION_FAILED}: {details}");

    public static CertificatePinningFailure InitializationExceptionOccurred(Exception ex) =>
        new(CertificatePinningFailureType.INITIALIZATION_EXCEPTION, $"{CertificatePinningFailureMessages.INITIALIZATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure CertificateValidationFailed(string details) =>
        new(CertificatePinningFailureType.CERTIFICATE_VALIDATION_FAILED, $"{CertificatePinningFailureMessages.CERTIFICATE_VALIDATION_FAILED}: {details}");

    public static CertificatePinningFailure CertificateValidationExceptionOccurred(Exception ex) =>
        new(CertificatePinningFailureType.CERTIFICATE_VALIDATION_EXCEPTION, $"{CertificatePinningFailureMessages.CERTIFICATE_VALIDATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure PlaintextRequired() =>
        new(CertificatePinningFailureType.PLAINTEXT_REQUIRED, CertificatePinningFailureMessages.PLAINTEXT_REQUIRED);

    public static CertificatePinningFailure RsaEncryptionFailed(string details) =>
        new(CertificatePinningFailureType.RSA_ENCRYPTION_FAILED, $"{CertificatePinningFailureMessages.RSA_ENCRYPTION_FAILED}: {details}");

    public static CertificatePinningFailure RsaEncryptionExceptionOccurred(Exception ex) =>
        new(CertificatePinningFailureType.RSA_ENCRYPTION_EXCEPTION, $"{CertificatePinningFailureMessages.RSA_ENCRYPTION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure CiphertextRequired() =>
        new(CertificatePinningFailureType.CIPHERTEXT_REQUIRED, CertificatePinningFailureMessages.CIPHERTEXT_REQUIRED);

    public static CertificatePinningFailure RsaDecryptionFailed(string details) =>
        new(CertificatePinningFailureType.RSA_DECRYPTION_FAILED, $"{CertificatePinningFailureMessages.RSA_DECRYPTION_FAILED}: {details}");

    public static CertificatePinningFailure RsaDecryptionExceptionOccurred(Exception ex) =>
        new(CertificatePinningFailureType.RSA_DECRYPTION_EXCEPTION, $"{CertificatePinningFailureMessages.RSA_DECRYPTION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure MessageRequired() =>
        new(CertificatePinningFailureType.MESSAGE_REQUIRED, CertificatePinningFailureMessages.MESSAGE_REQUIRED);

    public static CertificatePinningFailure InvalidSignatureSize(int expectedSize) =>
        new(CertificatePinningFailureType.INVALID_SIGNATURE_SIZE, $"Signature must be {expectedSize} bytes");

    public static CertificatePinningFailure Ed25519VerificationError(string details) =>
        new(CertificatePinningFailureType.ED_25519_VERIFICATION_ERROR, $"{CertificatePinningFailureMessages.ED_25519_VERIFICATION_ERROR}: {details}");

    public static CertificatePinningFailure Ed25519VerificationExceptionOccurred(Exception ex) =>
        new(CertificatePinningFailureType.ED_25519_VERIFICATION_EXCEPTION, $"{CertificatePinningFailureMessages.ED_25519_VERIFICATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure ServiceInitializing() =>
        new(CertificatePinningFailureType.SERVICE_INITIALIZING, CertificatePinningFailureMessages.SERVICE_INITIALIZING);

    public static CertificatePinningFailure ServiceInvalidState() =>
        new(CertificatePinningFailureType.SERVICE_INVALID_STATE, CertificatePinningFailureMessages.SERVICE_INVALID_STATE);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL);
}
