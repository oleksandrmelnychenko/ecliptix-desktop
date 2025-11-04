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
    public const string PLAINTEXT_TOO_LARGE = "Plaintext too large for RSA encryption (max 214 bytes)";
    public const string RSA_ENCRYPTION_FAILED = "RSA encryption failed";
    public const string RSA_ENCRYPTION_EXCEPTION = "RSA encryption exception";
    public const string CIPHERTEXT_REQUIRED = "Ciphertext is required";
    public const string PRIVATE_KEY_REQUIRED = "Private key is required";
    public const string RSA_DECRYPTION_FAILED = "RSA decryption failed";
    public const string RSA_DECRYPTION_EXCEPTION = "RSA decryption exception";
    public const string INVALID_CIPHERTEXT_SIZE = "Invalid ciphertext size";
    public const string INVALID_KEY_SIZE = "Key must be 32 bytes";
    public const string AES_GCM_DECRYPTION_FAILED = "Decryption failed";
    public const string AES_GCM_DECRYPTION_EXCEPTION = "Decryption exception";
    public const string MESSAGE_REQUIRED = "Message is required";
    public const string INVALID_PRIVATE_KEY_SIZE = "Private key must be 32 bytes";
    public const string ED_25519_SIGNING_FAILED = "Signing failed";
    public const string ED_25519_SIGNING_EXCEPTION = "Signing exception";
    public const string INVALID_SIGNATURE_SIZE = "Signature must be 64 bytes";
    public const string ED_25519_VERIFICATION_ERROR = "Verification error";
    public const string ED_25519_VERIFICATION_EXCEPTION = "Verification exception";
    public const string INVALID_NONCE_SIZE = "Invalid nonce size";
    public const string RANDOM_BYTES_GENERATION_FAILED = "Random bytes generation failed";
    public const string RANDOM_BYTES_GENERATION_EXCEPTION = "Nonce generation exception";
    public const string LIBRARY_CLEANUP_ERROR = "ERROR during native library cleanup";
    public const string SECURE_MEMORY_ALLOCATION_FAILED = "Secure memory allocation failed";
    public const string SECURE_MEMORY_WRITE_FAILED = "Failed to write to secure memory";
    public const string SECURE_MEMORY_READ_FAILED = "Failed to read from secure memory";
    public const string NATIVE_LIBRARY_NOT_FOUND = "Native library not found";
    public const string NATIVE_OPERATION_FAILED = "Native operation failed";
    public const string SERVICE_INITIALIZING = "Service is currently initializing";
    public const string SERVICE_INVALID_STATE = "Service is in an invalid state";
}
