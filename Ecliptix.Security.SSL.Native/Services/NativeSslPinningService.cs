using System.Runtime.InteropServices;
using System.Text;
using Ecliptix.Security.SSL.Native.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.SslPinning;

namespace Ecliptix.Security.SSL.Native.Services;

public sealed class NativeSslPinningService : IDisposable
{
    private volatile bool _isInitialized;
    private volatile bool _disposed;

    public NativeSslPinningService()
    {
    }

    public Task<Result<Unit, SslPinningFailure>> InitializeAsync()
    {
        return Task.Run(InitializeSync);
    }

    private Result<Unit, SslPinningFailure> InitializeSync()
    {
        if (_disposed)
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (_isInitialized)
            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);

        try
        {
            unsafe
            {
                EcliptixResult result = EcliptixNativeLibrary.Initialize();

                if (result != EcliptixResult.Success)
                {
                    string error = GetErrorString(result);
                    return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.LibraryInitializationFailed(error));
                }

                int initialized = EcliptixNativeLibrary.IsInitialized();
                if (initialized == 0)
                {
                }
            }

            _isInitialized = true;

            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.InitializationException(ex));
        }
    }

    public Task<Result<Unit, SslPinningFailure>> ValidateCertificateAsync(byte[] certificateData, string hostname)
    {
        return Task.Run(() => ValidateCertificateSync(certificateData, hostname));
    }

    private Result<Unit, SslPinningFailure> ValidateCertificateSync(byte[] certificateData, string hostname)
    {
        if (!_isInitialized)
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (certificateData == null || certificateData.Length == 0)
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.CertificateDataRequired());

        if (string.IsNullOrWhiteSpace(hostname))
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.HostnameRequired());

        try
        {
            unsafe
            {
                fixed (byte* certPtr = certificateData)
                {
                    IntPtr hostnamePtr = Marshal.StringToHGlobalAnsi(hostname);
                    try
                    {
                        EcliptixResult result = EcliptixNativeLibrary.ValidateCertificate(
                            certPtr, (nuint)certificateData.Length,
                            (byte*)hostnamePtr, 0x07);

                        if (result != EcliptixResult.Success)
                        {
                            string error = GetErrorString(result);
                            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationFailed(error));
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(hostnamePtr);
                    }
                }
            }

            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> EncryptAsync(byte[] plaintext)
    {
        return Task.Run(() => EncryptSync(plaintext));
    }

    private Result<byte[], SslPinningFailure> EncryptSync(byte[] plaintext)
    {
        if (!_isInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (plaintext == null || plaintext.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.PlaintextRequired());

        const int maxPlaintextSize = 214;
        if (plaintext.Length > maxPlaintextSize)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.PlaintextTooLarge());

        try
        {
            unsafe
            {
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
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RsaEncryptionFailed(error));
                    }
                }

                Array.Resize(ref ciphertext, (int)ciphertextSize);
                return Result<byte[], SslPinningFailure>.Ok(ciphertext);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RsaEncryptionException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> DecryptRsaAsync(byte[] ciphertext, byte[] privateKeyPem)
    {
        return Task.Run(() => DecryptRsaSync(ciphertext, privateKeyPem));
    }

    private Result<byte[], SslPinningFailure> DecryptRsaSync(byte[] ciphertext, byte[] privateKeyPem)
    {
        if (!_isInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (ciphertext == null || ciphertext.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.CiphertextRequired());

        if (privateKeyPem == null || privateKeyPem.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.PrivateKeyRequired());

        try
        {
            unsafe
            {
                const int maxPlaintextSize = 214;
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
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RsaDecryptionFailed(error));
                    }
                }

                Array.Resize(ref plaintext, (int)plaintextSize);
                return Result<byte[], SslPinningFailure>.Ok(plaintext);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RsaDecryptionException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> DecryptAsync(byte[] ciphertext, byte[] key)
    {
        return Task.Run(() => DecryptSync(ciphertext, key));
    }

    private Result<byte[], SslPinningFailure> DecryptSync(byte[] ciphertext, byte[] key)
    {
        if (!_isInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (ciphertext == null || ciphertext.Length <= EcliptixConstants.AesGcmIvSize + EcliptixConstants.AesGcmTagSize)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.InvalidCiphertextSize());

        if (key == null || key.Length != EcliptixConstants.AesGcmKeySize)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.InvalidKeySize(EcliptixConstants.AesGcmKeySize));

        try
        {
            unsafe
            {
                int maxPlaintextSize = ciphertext.Length - EcliptixConstants.AesGcmIvSize - EcliptixConstants.AesGcmTagSize;
                byte[] output = new byte[maxPlaintextSize];
                nuint actualOutputSize = (nuint)maxPlaintextSize;

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
                        null, 0,
                        noncePtr, (nuint)EcliptixConstants.AesGcmIvSize,
                        EcliptixConstants.AlgorithmAesGcm,
                        outputPtr, &actualOutputSize);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.AesGcmDecryptionFailed(error));
                    }
                }

                Array.Resize(ref output, (int)actualOutputSize);
                return Result<byte[], SslPinningFailure>.Ok(output);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.AesGcmDecryptionException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> SignEd25519Async(byte[] message, byte[] privateKey)
    {
        return Task.Run(() => SignEd25519Sync(message, privateKey));
    }

    private Result<byte[], SslPinningFailure> SignEd25519Sync(byte[] message, byte[] privateKey)
    {
        if (!_isInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (message == null || message.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.MessageRequired());

        if (privateKey == null || privateKey.Length != EcliptixConstants.Ed25519PrivateKeySize)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.InvalidPrivateKeySize(EcliptixConstants.Ed25519PrivateKeySize));

        try
        {
            unsafe
            {
                byte[] signature = new byte[EcliptixConstants.Ed25519SignatureSize];

                fixed (byte* messagePtr = message)
                fixed (byte* sigPtr = signature)
                {
                    EcliptixResult result = EcliptixNativeLibrary.SignEd25519(
                        messagePtr, (nuint)message.Length,
                        sigPtr);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.Ed25519SigningFailed(error));
                    }
                }

                return Result<byte[], SslPinningFailure>.Ok(signature);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.Ed25519SigningException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> VerifyEd25519Async(byte[] message, byte[] signature)
    {
        return Task.Run(() => VerifyEd25519Sync(message, signature));
    }

    private Result<bool, SslPinningFailure> VerifyEd25519Sync(byte[] message, byte[] signature)
    {
        if (!_isInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (message == null || message.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.MessageRequired());

        if (signature == null || signature.Length != EcliptixConstants.Ed25519SignatureSize)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.InvalidSignatureSize(EcliptixConstants.Ed25519SignatureSize));

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
                        return Result<bool, SslPinningFailure>.Ok(true);
                    }
                    else if (result == EcliptixResult.ErrorVerificationFailed)
                    {
                        return Result<bool, SslPinningFailure>.Ok(false);
                    }
                    else
                    {
                        string error = GetErrorString(result);
                        return Result<bool, SslPinningFailure>.Err(SslPinningFailure.Ed25519VerificationError(error));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.Ed25519VerificationException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> GenerateNonceAsync(int size = EcliptixConstants.AesGcmIvSize)
    {
        return Task.Run(() => GenerateNonceSync(size));
    }

    private Result<byte[], SslPinningFailure> GenerateNonceSync(int size)
    {
        if (!_isInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (_disposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (size <= 0 || size > 1024)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.InvalidNonceSize());

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
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RandomBytesGenerationFailed(error));
                    }
                }

                return Result<byte[], SslPinningFailure>.Ok(nonce);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RandomBytesGenerationException(ex));
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

                Console.WriteLine($"[Native SSL] Error {errorInfo->ErrorCode}: {message} at {sourceFile}:{errorInfo->SourceLine}");
            }
        }
        catch
        {
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
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            _disposed = true;
            _isInitialized = false;
        }
    }
}