namespace Ecliptix.Utilities.Failures.CertificatePinning;

public static class CertificatePinningFailureMessages
{
    public const string ServiceNotInitialized = "Service not initialized";
    public const string ServiceDisposed = "Service has been disposed";
    public const string LibraryInitializationFailed = "Library initialization failed";
    public const string InitializationException = "Initialization exception";
    public const string CertificateDataRequired = "Certificate data is required";
    public const string HostnameRequired = "Hostname is required";
    public const string CertificateValidationFailed = "Certificate validation failed";
    public const string CertificateValidationException = "Validation exception";
    public const string PlaintextRequired = "Plaintext is required";
    public const string PlaintextTooLarge = "Plaintext too large for RSA encryption (max 214 bytes)";
    public const string RsaEncryptionFailed = "RSA encryption failed";
    public const string RsaEncryptionException = "RSA encryption exception";
    public const string CiphertextRequired = "Ciphertext is required";
    public const string PrivateKeyRequired = "Private key is required";
    public const string RsaDecryptionFailed = "RSA decryption failed";
    public const string RsaDecryptionException = "RSA decryption exception";
    public const string InvalidCiphertextSize = "Invalid ciphertext size";
    public const string InvalidKeySize = "Key must be 32 bytes";
    public const string AesGcmDecryptionFailed = "Decryption failed";
    public const string AesGcmDecryptionException = "Decryption exception";
    public const string MessageRequired = "Message is required";
    public const string InvalidPrivateKeySize = "Private key must be 32 bytes";
    public const string Ed25519SigningFailed = "Signing failed";
    public const string Ed25519SigningException = "Signing exception";
    public const string InvalidSignatureSize = "Signature must be 64 bytes";
    public const string Ed25519VerificationError = "Verification error";
    public const string Ed25519VerificationException = "Verification exception";
    public const string InvalidNonceSize = "Invalid nonce size";
    public const string RandomBytesGenerationFailed = "Random bytes generation failed";
    public const string RandomBytesGenerationException = "Nonce generation exception";
    public const string LibraryCleanupError = "Error during native library cleanup";
    public const string SecureMemoryAllocationFailed = "Secure memory allocation failed";
    public const string SecureMemoryWriteFailed = "Failed to write to secure memory";
    public const string SecureMemoryReadFailed = "Failed to read from secure memory";
    public const string NativeLibraryNotFound = "Native library not found";
    public const string NativeOperationFailed = "Native operation failed";
    public const string ServiceInitializing = "Service is currently initializing";
    public const string ServiceInvalidState = "Service is in an invalid state";
}
