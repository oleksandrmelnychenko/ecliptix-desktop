namespace Ecliptix.Utilities.Failures.CertificatePinning;

public static class CertificatePinningFailureMessages
{
    public const string SERVICE_NOT_INITIALIZED = "Service not initialized";
    public const string SERVICE_DISPOSED = "Service has been disposed";
    public const string LIBRARY_INITIALIZATION_FAILED = "LIBRARY initialization failed";
    public const string INITIALIZATION_EXCEPTION = "Initialization exception";
    public const string CERTIFICATE_DATA_REQUIRED = "Certificate data is required";
    public const string HOSTNAME_REQUIRED = "Hostname is required";
    public const string CERTIFICATE_VALIDATION_FAILED = "Certificate validation failed";
    public const string CERTIFICATE_VALIDATION_EXCEPTION = "VALIDATION exception";
    public const string PLAINTEXT_REQUIRED = "Plaintext is required";
    public const string RSA_ENCRYPTION_FAILED = "RSA encryption failed";
    public const string RSA_ENCRYPTION_EXCEPTION = "RSA encryption exception";
    public const string CIPHERTEXT_REQUIRED = "Ciphertext is required";
    public const string RSA_DECRYPTION_FAILED = "RSA decryption failed";
    public const string RSA_DECRYPTION_EXCEPTION = "RSA decryption exception";
    public const string MESSAGE_REQUIRED = "Message is required";
    public const string ED_25519_VERIFICATION_ERROR = "Verification error";
    public const string ED_25519_VERIFICATION_EXCEPTION = "Verification exception";
    public const string SERVICE_INITIALIZING = "Service is currently initializing";
    public const string SERVICE_INVALID_STATE = "Service is in an invalid state";
}
