using System.Runtime.InteropServices;
using System.Text;
using Ecliptix.Security.SSL.Native.Native;
using Ecliptix.Utilities;
using Microsoft.Extensions.Logging;

namespace Ecliptix.Security.SSL.Native.Services;

/// <summary>
/// SSL certificate pinning service using the native Ecliptix security library
/// </summary>
public sealed class NativeSslPinningService : IDisposable
{
    private readonly ILogger<NativeSslPinningService> _logger;
    private volatile bool _isInitialized;
    private volatile bool _disposed;

    public NativeSslPinningService(ILogger<NativeSslPinningService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Initialize the native SSL pinning library
    /// </summary>
    public Task<Result<Unit, string>> InitializeAsync()
    {
        return Task.Run(InitializeSync);
    }

    private Result<Unit, string> InitializeSync()
    {
        if (_disposed)
            return Result<Unit, string>.Err("Service has been disposed");

        if (_isInitialized)
            return Result<Unit, string>.Ok(Unit.Value);

        try
        {
            _logger.LogInformation("Initializing native SSL pinning library");

            unsafe
            {
                // Initialize the library
                EcliptixResult result = EcliptixNativeLibrary.Initialize();

                if (result != EcliptixResult.Success)
                {
                    string error = GetErrorString(result);
                    _logger.LogError("Failed to initialize native SSL library: {Error}", error);
                    return Result<Unit, string>.Err($"Library initialization failed: {error}");
                }

                // Verify library is initialized
                int initialized = EcliptixNativeLibrary.IsInitialized();
                if (initialized == 0)
                {
                    _logger.LogWarning("Native SSL library initialization status unclear");
                    // Continue anyway - this is not critical
                }
            }

            _isInitialized = true;
            _logger.LogInformation("Native SSL pinning library initialized successfully");

            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during native SSL library initialization");
            return Result<Unit, string>.Err($"Initialization exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Validate and pin an SSL certificate
    /// </summary>
    public Task<Result<Unit, string>> ValidateCertificateAsync(byte[] certificateData, string hostname)
    {
        return Task.Run(() => ValidateCertificateSync(certificateData, hostname));
    }

    private Result<Unit, string> ValidateCertificateSync(byte[] certificateData, string hostname)
    {
        if (!_isInitialized)
            return Result<Unit, string>.Err("Service not initialized");

        if (_disposed)
            return Result<Unit, string>.Err("Service has been disposed");

        if (certificateData == null || certificateData.Length == 0)
            return Result<Unit, string>.Err("Certificate data is required");

        if (string.IsNullOrWhiteSpace(hostname))
            return Result<Unit, string>.Err("Hostname is required");

        try
        {
            unsafe
            {
                fixed (byte* certPtr = certificateData)
                {
                    // Use Marshal.StringToHGlobalAnsi for C string
                    IntPtr hostnamePtr = Marshal.StringToHGlobalAnsi(hostname);
                    try
                    {
                        // Use 0x07 for all validation flags: time + hostname + pin
                        EcliptixResult result = EcliptixNativeLibrary.ValidateCertificate(
                            certPtr, (nuint)certificateData.Length,
                            (byte*)hostnamePtr, 0x07);

                        if (result != EcliptixResult.Success)
                        {
                            string error = GetErrorString(result);
                            _logger.LogWarning("Certificate validation failed for {Hostname}: {Error}", hostname, error);
                            return Result<Unit, string>.Err($"Certificate validation failed: {error}");
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(hostnamePtr);
                    }
                }
            }

            _logger.LogDebug("Certificate validation successful for {Hostname}", hostname);
            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during certificate validation for {Hostname}", hostname);
            return Result<Unit, string>.Err($"Validation exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Encrypt data using embedded RSA public key from server certificate
    /// </summary>
    public Task<Result<byte[], string>> EncryptAsync(byte[] plaintext)
    {
        return Task.Run(() => EncryptSync(plaintext));
    }

    private Result<byte[], string> EncryptSync(byte[] plaintext)
    {
        if (!_isInitialized)
            return Result<byte[], string>.Err("Service not initialized");

        if (_disposed)
            return Result<byte[], string>.Err("Service has been disposed");

        if (plaintext == null || plaintext.Length == 0)
            return Result<byte[], string>.Err("Plaintext is required");

        // RSA max plaintext size (2048-bit key with OAEP padding)
        const int maxPlaintextSize = 214; // 256 - 42 bytes OAEP padding
        if (plaintext.Length > maxPlaintextSize)
            return Result<byte[], string>.Err($"Plaintext too large for RSA encryption (max {maxPlaintextSize} bytes)");

        try
        {
            unsafe
            {
                // Allocate buffer for RSA ciphertext (2048-bit = 256 bytes)
                const int rsaKeySize = 256;
                byte[] ciphertext = new byte[rsaKeySize];
                nuint ciphertextSize = (nuint)rsaKeySize;

                fixed (byte* plaintextPtr = plaintext)
                fixed (byte* ciphertextPtr = ciphertext)
                {
                    EcliptixResult result = EcliptixNativeLibrary.EncryptRsa(
                        plaintextPtr, (nuint)plaintext.Length,
                        ciphertextPtr, &ciphertextSize);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        _logger.LogError("RSA encryption failed: {Error}", error);
                        return Result<byte[], string>.Err($"RSA encryption failed: {error}");
                    }
                }

                // Resize to actual output size
                Array.Resize(ref ciphertext, (int)ciphertextSize);
                return Result<byte[], string>.Ok(ciphertext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during RSA encryption");
            return Result<byte[], string>.Err($"RSA encryption exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrypt data using RSA private key (server-side only)
    /// </summary>
    public Task<Result<byte[], string>> DecryptRsaAsync(byte[] ciphertext, byte[] privateKeyPem)
    {
        return Task.Run(() => DecryptRsaSync(ciphertext, privateKeyPem));
    }

    private Result<byte[], string> DecryptRsaSync(byte[] ciphertext, byte[] privateKeyPem)
    {
        if (!_isInitialized)
            return Result<byte[], string>.Err("Service not initialized");

        if (_disposed)
            return Result<byte[], string>.Err("Service has been disposed");

        if (ciphertext == null || ciphertext.Length == 0)
            return Result<byte[], string>.Err("Ciphertext is required");

        if (privateKeyPem == null || privateKeyPem.Length == 0)
            return Result<byte[], string>.Err("Private key is required");

        try
        {
            unsafe
            {
                // Allocate buffer for decrypted plaintext (max RSA key size)
                const int maxPlaintextSize = 214; // Max for 2048-bit RSA with OAEP
                byte[] plaintext = new byte[maxPlaintextSize];
                nuint plaintextSize = (nuint)maxPlaintextSize;

                fixed (byte* ciphertextPtr = ciphertext)
                fixed (byte* privateKeyPtr = privateKeyPem)
                fixed (byte* plaintextPtr = plaintext)
                {
                    EcliptixResult result = EcliptixNativeLibrary.DecryptRsa(
                        ciphertextPtr, (nuint)ciphertext.Length,
                        privateKeyPtr, (nuint)privateKeyPem.Length,
                        plaintextPtr, &plaintextSize);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        _logger.LogError("RSA decryption failed: {Error}", error);
                        return Result<byte[], string>.Err($"RSA decryption failed: {error}");
                    }
                }

                // Resize to actual output size
                Array.Resize(ref plaintext, (int)plaintextSize);
                return Result<byte[], string>.Ok(plaintext);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during RSA decryption");
            return Result<byte[], string>.Err($"RSA decryption exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Decrypt data using AES-GCM
    /// </summary>
    public Task<Result<byte[], string>> DecryptAsync(byte[] ciphertext, byte[] key)
    {
        return Task.Run(() => DecryptSync(ciphertext, key));
    }

    private Result<byte[], string> DecryptSync(byte[] ciphertext, byte[] key)
    {
        if (!_isInitialized)
            return Result<byte[], string>.Err("Service not initialized");

        if (_disposed)
            return Result<byte[], string>.Err("Service has been disposed");

        if (ciphertext == null || ciphertext.Length <= EcliptixConstants.AesGcmIvSize + EcliptixConstants.AesGcmTagSize)
            return Result<byte[], string>.Err("Invalid ciphertext size");

        if (key == null || key.Length != EcliptixConstants.AesGcmKeySize)
            return Result<byte[], string>.Err($"Key must be {EcliptixConstants.AesGcmKeySize} bytes");

        try
        {
            unsafe
            {
                // Allocate buffer for plaintext
                int maxPlaintextSize = ciphertext.Length - EcliptixConstants.AesGcmIvSize - EcliptixConstants.AesGcmTagSize;
                byte[] output = new byte[maxPlaintextSize];
                nuint actualOutputSize = (nuint)maxPlaintextSize;

                // Extract nonce and tag from combined ciphertext
                int actualCiphertextSize = ciphertext.Length - EcliptixConstants.AesGcmIvSize - EcliptixConstants.AesGcmTagSize;
                byte[] actualCiphertext = new byte[actualCiphertextSize];
                byte[] nonce = new byte[EcliptixConstants.AesGcmIvSize];
                byte[] tag = new byte[EcliptixConstants.AesGcmTagSize];

                Array.Copy(ciphertext, 0, actualCiphertext, 0, actualCiphertextSize);
                Array.Copy(ciphertext, actualCiphertextSize, nonce, 0, EcliptixConstants.AesGcmIvSize);
                Array.Copy(ciphertext, actualCiphertextSize + EcliptixConstants.AesGcmIvSize, tag, 0, EcliptixConstants.AesGcmTagSize);

                fixed (byte* ciphertextPtr = actualCiphertext)
                fixed (byte* keyPtr = key)
                fixed (byte* outputPtr = output)
                fixed (byte* noncePtr = nonce)
                fixed (byte* tagPtr = tag)
                {
                    EcliptixResult result = EcliptixNativeLibrary.DecryptAead(
                        ciphertextPtr, (nuint)actualCiphertextSize,
                        tagPtr, (nuint)EcliptixConstants.AesGcmTagSize,
                        keyPtr, (nuint)key.Length,
                        null, 0, // no additional data
                        noncePtr, (nuint)EcliptixConstants.AesGcmIvSize,
                        EcliptixConstants.AlgorithmAesGcm,
                        outputPtr, &actualOutputSize);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        _logger.LogError("Decryption failed: {Error}", error);
                        return Result<byte[], string>.Err($"Decryption failed: {error}");
                    }
                }

                // Resize to actual output size
                Array.Resize(ref output, (int)actualOutputSize);
                return Result<byte[], string>.Ok(output);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during decryption");
            return Result<byte[], string>.Err($"Decryption exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Sign data using Ed25519
    /// </summary>
    public Task<Result<byte[], string>> SignEd25519Async(byte[] message, byte[] privateKey)
    {
        return Task.Run(() => SignEd25519Sync(message, privateKey));
    }

    private Result<byte[], string> SignEd25519Sync(byte[] message, byte[] privateKey)
    {
        if (!_isInitialized)
            return Result<byte[], string>.Err("Service not initialized");

        if (_disposed)
            return Result<byte[], string>.Err("Service has been disposed");

        if (message == null || message.Length == 0)
            return Result<byte[], string>.Err("Message is required");

        if (privateKey == null || privateKey.Length != EcliptixConstants.Ed25519PrivateKeySize)
            return Result<byte[], string>.Err($"Private key must be {EcliptixConstants.Ed25519PrivateKeySize} bytes");

        try
        {
            unsafe
            {
                byte[] signature = new byte[EcliptixConstants.Ed25519SignatureSize];

                fixed (byte* messagePtr = message)
                fixed (byte* sigPtr = signature)
                {
                    // The native library uses embedded private key
                    EcliptixResult result = EcliptixNativeLibrary.SignEd25519(
                        messagePtr, (nuint)message.Length,
                        sigPtr);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        _logger.LogError("Ed25519 signing failed: {Error}", error);
                        return Result<byte[], string>.Err($"Signing failed: {error}");
                    }
                }

                return Result<byte[], string>.Ok(signature);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Ed25519 signing");
            return Result<byte[], string>.Err($"Signing exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Verify Ed25519 signature using embedded public key
    /// </summary>
    public Task<Result<bool, string>> VerifyEd25519Async(byte[] message, byte[] signature)
    {
        return Task.Run(() => VerifyEd25519Sync(message, signature));
    }

    private Result<bool, string> VerifyEd25519Sync(byte[] message, byte[] signature)
    {
        if (!_isInitialized)
            return Result<bool, string>.Err("Service not initialized");

        if (_disposed)
            return Result<bool, string>.Err("Service has been disposed");

        if (message == null || message.Length == 0)
            return Result<bool, string>.Err("Message is required");

        if (signature == null || signature.Length != EcliptixConstants.Ed25519SignatureSize)
            return Result<bool, string>.Err($"Signature must be {EcliptixConstants.Ed25519SignatureSize} bytes");

        try
        {
            unsafe
            {
                fixed (byte* messagePtr = message)
                fixed (byte* sigPtr = signature)
                {
                    EcliptixResult result = EcliptixNativeLibrary.VerifyEd25519(
                        messagePtr, (nuint)message.Length, sigPtr);

                    if (result == EcliptixResult.Success)
                    {
                        return Result<bool, string>.Ok(true);
                    }
                    else if (result == EcliptixResult.ErrorVerificationFailed)
                    {
                        return Result<bool, string>.Ok(false);
                    }
                    else
                    {
                        string error = GetErrorString(result);
                        _logger.LogError("Ed25519 verification error: {Error}", error);
                        return Result<bool, string>.Err($"Verification error: {error}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during Ed25519 verification");
            return Result<bool, string>.Err($"Verification exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Generate a cryptographically secure random nonce
    /// </summary>
    public Task<Result<byte[], string>> GenerateNonceAsync(int size = EcliptixConstants.AesGcmIvSize)
    {
        return Task.Run(() => GenerateNonceSync(size));
    }

    private Result<byte[], string> GenerateNonceSync(int size)
    {
        if (!_isInitialized)
            return Result<byte[], string>.Err("Service not initialized");

        if (_disposed)
            return Result<byte[], string>.Err("Service has been disposed");

        if (size <= 0 || size > 1024)
            return Result<byte[], string>.Err("Invalid nonce size");

        try
        {
            unsafe
            {
                byte[] nonce = new byte[size];

                fixed (byte* noncePtr = nonce)
                {
                    EcliptixResult result = EcliptixNativeLibrary.GenerateRandom(noncePtr, (nuint)size);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        _logger.LogError("Random bytes generation failed: {Error}", error);
                        return Result<byte[], string>.Err($"Random bytes generation failed: {error}");
                    }
                }

                return Result<byte[], string>.Ok(nonce);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during nonce generation");
            return Result<byte[], string>.Err($"Nonce generation exception: {ex.Message}");
        }
    }

    private unsafe string GetErrorString(EcliptixResult result)
    {
        try
        {
            byte* errorPtr = EcliptixNativeLibrary.GetErrorMessage();
            if (errorPtr != null)
            {
                return Marshal.PtrToStringUTF8((IntPtr)errorPtr) ?? $"Unknown error: {result}";
            }
        }
        catch
        {
            // Ignore errors getting error string
        }

        return $"Error code: {result}";
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
    private static unsafe void OnNativeError(EcliptixErrorInfo* errorInfo, void* userData)
    {
        try
        {
            if (errorInfo != null && errorInfo->ErrorMessage != null)
            {
                string message = Marshal.PtrToStringUTF8((IntPtr)errorInfo->ErrorMessage, (int)errorInfo->ErrorMessageLength)
                                ?? "Unknown error";
                string sourceFile = errorInfo->SourceFile != null
                                  ? Marshal.PtrToStringUTF8((IntPtr)errorInfo->SourceFile) ?? "unknown"
                                  : "unknown";

                // Log to console for now - in production, use proper logging
                Console.WriteLine($"[Native SSL] Error {errorInfo->ErrorCode}: {message} at {sourceFile}:{errorInfo->SourceLine}");
            }
        }
        catch
        {
            // Ignore errors in error callback to prevent recursion
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            if (_isInitialized)
            {
                EcliptixNativeLibrary.Cleanup();
                _logger.LogInformation("Native SSL pinning library cleaned up");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during native library cleanup");
        }
        finally
        {
            _disposed = true;
            _isInitialized = false;
        }
    }
}