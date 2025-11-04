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

    public static CertificatePinningFailure SERVICE_NOT_INITIALIZED() =>
        new(CertificatePinningFailureType.SERVICE_NOT_INITIALIZED, CertificatePinningFailureMessages.SERVICE_NOT_INITIALIZED);

    public static CertificatePinningFailure SERVICE_DISPOSED() =>
        new(CertificatePinningFailureType.SERVICE_DISPOSED, CertificatePinningFailureMessages.SERVICE_DISPOSED);

    public static CertificatePinningFailure LIBRARY_INITIALIZATION_FAILED(string details) =>
        new(CertificatePinningFailureType.LIBRARY_INITIALIZATION_FAILED, $"{CertificatePinningFailureMessages.LIBRARY_INITIALIZATION_FAILED}: {details}");

    public static CertificatePinningFailure INITIALIZATION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.INITIALIZATION_EXCEPTION, $"{CertificatePinningFailureMessages.INITIALIZATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure CERTIFICATE_DATA_REQUIRED() =>
        new(CertificatePinningFailureType.CERTIFICATE_DATA_REQUIRED, CertificatePinningFailureMessages.CERTIFICATE_DATA_REQUIRED);

    public static CertificatePinningFailure HOSTNAME_REQUIRED() =>
        new(CertificatePinningFailureType.HOSTNAME_REQUIRED, CertificatePinningFailureMessages.HOSTNAME_REQUIRED);

    public static CertificatePinningFailure CERTIFICATE_VALIDATION_FAILED(string details) =>
        new(CertificatePinningFailureType.CERTIFICATE_VALIDATION_FAILED, $"{CertificatePinningFailureMessages.CERTIFICATE_VALIDATION_FAILED}: {details}");

    public static CertificatePinningFailure CERTIFICATE_VALIDATION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.CERTIFICATE_VALIDATION_EXCEPTION, $"{CertificatePinningFailureMessages.CERTIFICATE_VALIDATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure PLAINTEXT_REQUIRED() =>
        new(CertificatePinningFailureType.PLAINTEXT_REQUIRED, CertificatePinningFailureMessages.PLAINTEXT_REQUIRED);

    public static CertificatePinningFailure PLAINTEXT_TOO_LARGE() =>
        new(CertificatePinningFailureType.PLAINTEXT_TOO_LARGE, CertificatePinningFailureMessages.PLAINTEXT_TOO_LARGE);

    public static CertificatePinningFailure RSA_ENCRYPTION_FAILED(string details) =>
        new(CertificatePinningFailureType.RSA_ENCRYPTION_FAILED, $"{CertificatePinningFailureMessages.RSA_ENCRYPTION_FAILED}: {details}");

    public static CertificatePinningFailure RSA_ENCRYPTION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.RSA_ENCRYPTION_EXCEPTION, $"{CertificatePinningFailureMessages.RSA_ENCRYPTION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure CIPHERTEXT_REQUIRED() =>
        new(CertificatePinningFailureType.CIPHERTEXT_REQUIRED, CertificatePinningFailureMessages.CIPHERTEXT_REQUIRED);

    public static CertificatePinningFailure PRIVATE_KEY_REQUIRED() =>
        new(CertificatePinningFailureType.PRIVATE_KEY_REQUIRED, CertificatePinningFailureMessages.PRIVATE_KEY_REQUIRED);

    public static CertificatePinningFailure RSA_DECRYPTION_FAILED(string details) =>
        new(CertificatePinningFailureType.RSA_DECRYPTION_FAILED, $"{CertificatePinningFailureMessages.RSA_DECRYPTION_FAILED}: {details}");

    public static CertificatePinningFailure RSA_DECRYPTION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.RSA_DECRYPTION_EXCEPTION, $"{CertificatePinningFailureMessages.RSA_DECRYPTION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure INVALID_CIPHERTEXT_SIZE() =>
        new(CertificatePinningFailureType.INVALID_CIPHERTEXT_SIZE, CertificatePinningFailureMessages.INVALID_CIPHERTEXT_SIZE);

    public static CertificatePinningFailure INVALID_KEY_SIZE(int expectedSize) =>
        new(CertificatePinningFailureType.INVALID_KEY_SIZE, $"Key must be {expectedSize} bytes");

    public static CertificatePinningFailure AES_GCM_DECRYPTION_FAILED(string details) =>
        new(CertificatePinningFailureType.AES_GCM_DECRYPTION_FAILED, $"{CertificatePinningFailureMessages.AES_GCM_DECRYPTION_FAILED}: {details}");

    public static CertificatePinningFailure AES_GCM_DECRYPTION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.AES_GCM_DECRYPTION_EXCEPTION, $"{CertificatePinningFailureMessages.AES_GCM_DECRYPTION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure MESSAGE_REQUIRED() =>
        new(CertificatePinningFailureType.MESSAGE_REQUIRED, CertificatePinningFailureMessages.MESSAGE_REQUIRED);

    public static CertificatePinningFailure INVALID_PRIVATE_KEY_SIZE(int expectedSize) =>
        new(CertificatePinningFailureType.INVALID_PRIVATE_KEY_SIZE, $"Private key must be {expectedSize} bytes");

    public static CertificatePinningFailure ED_25519_SIGNING_FAILED(string details) =>
        new(CertificatePinningFailureType.ED_25519_SIGNING_FAILED, $"{CertificatePinningFailureMessages.ED_25519_SIGNING_FAILED}: {details}");

    public static CertificatePinningFailure ED_25519_SIGNING_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.ED_25519_SIGNING_EXCEPTION, $"{CertificatePinningFailureMessages.ED_25519_SIGNING_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure INVALID_SIGNATURE_SIZE(int expectedSize) =>
        new(CertificatePinningFailureType.INVALID_SIGNATURE_SIZE, $"Signature must be {expectedSize} bytes");

    public static CertificatePinningFailure ED_25519_VERIFICATION_ERROR(string details) =>
        new(CertificatePinningFailureType.ED_25519_VERIFICATION_ERROR, $"{CertificatePinningFailureMessages.ED_25519_VERIFICATION_ERROR}: {details}");

    public static CertificatePinningFailure ED_25519_VERIFICATION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.ED_25519_VERIFICATION_EXCEPTION, $"{CertificatePinningFailureMessages.ED_25519_VERIFICATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure INVALID_NONCE_SIZE() =>
        new(CertificatePinningFailureType.INVALID_NONCE_SIZE, CertificatePinningFailureMessages.INVALID_NONCE_SIZE);

    public static CertificatePinningFailure RANDOM_BYTES_GENERATION_FAILED(string details) =>
        new(CertificatePinningFailureType.RANDOM_BYTES_GENERATION_FAILED, $"{CertificatePinningFailureMessages.RANDOM_BYTES_GENERATION_FAILED}: {details}");

    public static CertificatePinningFailure RANDOM_BYTES_GENERATION_EXCEPTION(Exception ex) =>
        new(CertificatePinningFailureType.RANDOM_BYTES_GENERATION_EXCEPTION, $"{CertificatePinningFailureMessages.RANDOM_BYTES_GENERATION_EXCEPTION}: {ex.Message}", ex);

    public static CertificatePinningFailure LIBRARY_CLEANUP_ERROR(Exception ex) =>
        new(CertificatePinningFailureType.LIBRARY_CLEANUP_ERROR, $"{CertificatePinningFailureMessages.LIBRARY_CLEANUP_ERROR}: {ex.Message}", ex);

    public static CertificatePinningFailure SECURE_MEMORY_ALLOCATION_FAILED(string details) =>
        new(CertificatePinningFailureType.SECURE_MEMORY_ALLOCATION_FAILED, $"{CertificatePinningFailureMessages.SECURE_MEMORY_ALLOCATION_FAILED}: {details}");

    public static CertificatePinningFailure SECURE_MEMORY_WRITE_FAILED(string details) =>
        new(CertificatePinningFailureType.SECURE_MEMORY_WRITE_FAILED, $"{CertificatePinningFailureMessages.SECURE_MEMORY_WRITE_FAILED}: {details}");

    public static CertificatePinningFailure SECURE_MEMORY_READ_FAILED(string details) =>
        new(CertificatePinningFailureType.SECURE_MEMORY_READ_FAILED, $"{CertificatePinningFailureMessages.SECURE_MEMORY_READ_FAILED}: {details}");

    public static CertificatePinningFailure NATIVE_LIBRARY_NOT_FOUND(string operationName, Exception ex) =>
        new(CertificatePinningFailureType.NATIVE_LIBRARY_NOT_FOUND, $"{CertificatePinningFailureMessages.NATIVE_LIBRARY_NOT_FOUND} during {operationName}: {ex.Message}", ex);

    public static CertificatePinningFailure NATIVE_OPERATION_FAILED(string operationName, Exception ex) =>
        new(CertificatePinningFailureType.NATIVE_OPERATION_FAILED, $"{CertificatePinningFailureMessages.NATIVE_OPERATION_FAILED} during {operationName}: {ex.Message}", ex);

    public static CertificatePinningFailure SERVICE_INITIALIZING() =>
        new(CertificatePinningFailureType.SERVICE_INITIALIZING, CertificatePinningFailureMessages.SERVICE_INITIALIZING);

    public static CertificatePinningFailure SERVICE_INVALID_STATE() =>
        new(CertificatePinningFailureType.SERVICE_INVALID_STATE, CertificatePinningFailureMessages.SERVICE_INVALID_STATE);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(ERROR_CODE.InternalError, StatusCode.Internal, ErrorI18nKeys.INTERNAL);
}
