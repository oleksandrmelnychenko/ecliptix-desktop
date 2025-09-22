/*
 * Ecliptix Security SSL Native Library
 * Client-side SSL certificate pinning service
 * Author: Oleksandr Melnychenko
 */

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Security.SSL.Native.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.SslPinning;

namespace Ecliptix.Security.SSL.Native.Services;

public sealed class SslPinningService : IDisposable, IAsyncDisposable
{
    private int _initialized;
    private int _disposed;

    private bool IsInitialized => Volatile.Read(ref _initialized) == 1;
    private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    public Task<Result<Unit, SslPinningFailure>> InitializeAsync(CancellationToken cancellationToken = default)
        => Task.Run(InitializeSync, cancellationToken);

    private Result<Unit, SslPinningFailure> InitializeSync()
    {
        if (IsDisposed)
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (IsInitialized)
            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);

        try
        {
            EcliptixResult result = EcliptixNativeLibrary.Initialize();
            if (result != EcliptixResult.Success)
            {
                string error = GetErrorString(result);
                return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.LibraryInitializationFailed(error));
            }

            Volatile.Write(ref _initialized, 1);
            return Result<Unit, SslPinningFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SslPinningFailure>.Err(SslPinningFailure.InitializationException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> ValidateCertificateAsync(byte[] certDer, string hostname, CancellationToken cancellationToken = default)
        => Task.Run(() => ValidateCertificateSync(certDer, hostname), cancellationToken);

    private Result<bool, SslPinningFailure> ValidateCertificateSync(byte[] certDer, string hostname)
    {
        if (IsDisposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (certDer == null || certDer.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateDataRequired());

        if (string.IsNullOrEmpty(hostname))
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.HostnameRequired());

        try
        {
            unsafe
            {
                byte[] hostnameBytes = Encoding.UTF8.GetBytes(hostname);

                fixed (byte* certPtr = certDer)
                fixed (byte* hostnamePtr = hostnameBytes)
                {
                    EcliptixResult result = EcliptixNativeLibrary.ValidateCertificate(
                        certPtr, (nuint)certDer.Length, hostnamePtr);

                    if (result == EcliptixResult.Success)
                        return Result<bool, SslPinningFailure>.Ok(true);

                    if (result == EcliptixResult.ErrorCertificateInvalid)
                        return Result<bool, SslPinningFailure>.Ok(false);

                    string error = GetErrorString(result);
                    return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationFailed(error));
                }
            }
        }
        catch (Exception ex)
        {
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationException(ex));
        }
    }

    public Task<Result<EcliptixPin, SslPinningFailure>> GetCertificatePinAsync(byte[] certDer, CancellationToken cancellationToken = default)
        => Task.Run(() => GetCertificatePinSync(certDer), cancellationToken);

    private Result<EcliptixPin, SslPinningFailure> GetCertificatePinSync(byte[] certDer)
    {
        if (IsDisposed)
            return Result<EcliptixPin, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<EcliptixPin, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (certDer == null || certDer.Length == 0)
            return Result<EcliptixPin, SslPinningFailure>.Err(SslPinningFailure.CertificateDataRequired());

        try
        {
            unsafe
            {
                EcliptixPin pin = new();

                fixed (byte* certPtr = certDer)
                {
                    EcliptixPin* pinPtr = &pin;
                    EcliptixResult result = EcliptixNativeLibrary.GetCertificatePin(
                        certPtr, (nuint)certDer.Length, pinPtr);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        return Result<EcliptixPin, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationFailed(error));
                    }
                }

                return Result<EcliptixPin, SslPinningFailure>.Ok(pin);
            }
        }
        catch (Exception ex)
        {
            return Result<EcliptixPin, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> VerifyCertificatePinAsync(byte[] certDer, string hostname, EcliptixPin expectedPin, CancellationToken cancellationToken = default)
        => Task.Run(() => VerifyCertificatePinSync(certDer, hostname, expectedPin), cancellationToken);

    private Result<bool, SslPinningFailure> VerifyCertificatePinSync(byte[] certDer, string hostname, EcliptixPin expectedPin)
    {
        if (IsDisposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (certDer == null || certDer.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateDataRequired());

        if (string.IsNullOrEmpty(hostname))
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.HostnameRequired());

        // Note: fixed buffer validation is handled by the struct definition

        try
        {
            unsafe
            {
                byte[] hostnameBytes = Encoding.UTF8.GetBytes(hostname);

                fixed (byte* certPtr = certDer)
                fixed (byte* hostnamePtr = hostnameBytes)
                {
                    EcliptixPin* expectedPinPtr = &expectedPin;
                    EcliptixResult result = EcliptixNativeLibrary.VerifyCertificatePin(
                        certPtr, (nuint)certDer.Length, hostnamePtr, expectedPinPtr);

                    if (result == EcliptixResult.Success)
                        return Result<bool, SslPinningFailure>.Ok(true);

                    if (result == EcliptixResult.ErrorPinVerificationFailed)
                        return Result<bool, SslPinningFailure>.Ok(false);

                    string error = GetErrorString(result);
                    return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationFailed(error));
                }
            }
        }
        catch (Exception ex)
        {
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> IsHostnameTrustedAsync(string hostname, CancellationToken cancellationToken = default)
        => Task.Run(() => IsHostnameTrustedSync(hostname), cancellationToken);

    private Result<bool, SslPinningFailure> IsHostnameTrustedSync(string hostname)
    {
        if (IsDisposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (string.IsNullOrEmpty(hostname))
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.HostnameRequired());

        try
        {
            unsafe
            {
                byte[] hostnameBytes = Encoding.UTF8.GetBytes(hostname);

                fixed (byte* hostnamePtr = hostnameBytes)
                {
                    int trusted = EcliptixNativeLibrary.IsHostnameTrusted(hostnamePtr);
                    return Result<bool, SslPinningFailure>.Ok(trusted == 1);
                }
            }
        }
        catch (Exception ex)
        {
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.CertificateValidationException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> EncryptRsaAsync(byte[] plaintext, CancellationToken cancellationToken = default)
        => Task.Run(() => EncryptRsaSync(plaintext), cancellationToken);

    public Task<Result<SodiumSecureMemoryHandle, SslPinningFailure>> EncryptRsaSecureAsync(byte[] plaintext, CancellationToken cancellationToken = default)
        => Task.Run(() => EncryptRsaSecureSync(plaintext), cancellationToken);

    private Result<byte[], SslPinningFailure> EncryptRsaSync(byte[] plaintext)
    {
        if (IsDisposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (plaintext == null || plaintext.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.PlaintextRequired());

        if (plaintext.Length > EcliptixConstants.RsaMaxPlaintextSize)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.PlaintextTooLarge());

        try
        {
            unsafe
            {
                byte[] ciphertext = new byte[EcliptixConstants.RsaCiphertextSize];
                nuint ciphertextSize = EcliptixConstants.RsaCiphertextSize;

                fixed (byte* plaintextPtr = plaintext)
                fixed (byte* ciphertextPtr = ciphertext)
                {
                    EcliptixResult result = EcliptixNativeLibrary.EncryptRSA(
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

    private Result<SodiumSecureMemoryHandle, SslPinningFailure> EncryptRsaSecureSync(byte[] plaintext)
    {
        if (IsDisposed)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (plaintext == null || plaintext.Length == 0)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.PlaintextRequired());

        if (plaintext.Length > EcliptixConstants.RsaMaxPlaintextSize)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.PlaintextTooLarge());

        try
        {
            unsafe
            {
                byte[] tempCiphertext = new byte[EcliptixConstants.RsaCiphertextSize];
                try
                {
                    nuint ciphertextSize = EcliptixConstants.RsaCiphertextSize;

                    fixed (byte* plaintextPtr = plaintext)
                    fixed (byte* ciphertextPtr = tempCiphertext)
                    {
                        EcliptixResult result = EcliptixNativeLibrary.EncryptRSA(
                            plaintextPtr, (nuint)plaintext.Length,
                            ciphertextPtr, &ciphertextSize);

                        if (result != EcliptixResult.Success)
                        {
                            string error = GetErrorString(result);
                            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.RsaEncryptionFailed(error));
                        }
                    }

                    var handleResult = SodiumSecureMemoryHandle.Allocate((int)ciphertextSize);
                    if (handleResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(
                            SslPinningFailure.SecureMemoryAllocationFailed(handleResult.UnwrapErr().Message));

                    SodiumSecureMemoryHandle handle = handleResult.Unwrap();

                    var writeResult = handle.Write(tempCiphertext.AsSpan(0, (int)ciphertextSize));
                    if (writeResult.IsErr)
                    {
                        handle.Dispose();
                        return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(
                            SslPinningFailure.SecureMemoryWriteFailed(writeResult.UnwrapErr().Message));
                    }

                    return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Ok(handle);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(tempCiphertext);
                }
            }
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.RsaEncryptionException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> VerifyEd25519SignatureAsync(byte[] message, byte[] signature, byte[] publicKey, CancellationToken cancellationToken = default)
        => Task.Run(() => VerifyEd25519SignatureSync(message, signature, publicKey), cancellationToken);

    private Result<bool, SslPinningFailure> VerifyEd25519SignatureSync(byte[] message, byte[] signature, byte[] publicKey)
    {
        if (IsDisposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (message == null || message.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.MessageRequired());

        if (signature == null || signature.Length != EcliptixConstants.Ed25519SignatureSize)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.InvalidSignatureSize(EcliptixConstants.Ed25519SignatureSize));

        if (publicKey == null || publicKey.Length != EcliptixConstants.Ed25519PublicKeySize)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.InvalidKeySize(EcliptixConstants.Ed25519PublicKeySize));

        try
        {
            unsafe
            {
                fixed (byte* messagePtr = message)
                fixed (byte* signaturePtr = signature)
                fixed (byte* publicKeyPtr = publicKey)
                {
                    EcliptixResult result = EcliptixNativeLibrary.VerifyEd25519Signature(
                        messagePtr, (nuint)message.Length,
                        signaturePtr, publicKeyPtr);

                    if (result == EcliptixResult.Success)
                        return Result<bool, SslPinningFailure>.Ok(true);

                    if (result == EcliptixResult.ErrorSignatureInvalid)
                        return Result<bool, SslPinningFailure>.Ok(false);

                    string error = GetErrorString(result);
                    return Result<bool, SslPinningFailure>.Err(SslPinningFailure.Ed25519VerificationError(error));
                }
            }
        }
        catch (Exception ex)
        {
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.Ed25519VerificationException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> HashSha256Async(byte[] data, CancellationToken cancellationToken = default)
        => Task.Run(() => HashSha256Sync(data), cancellationToken);

    private Result<byte[], SslPinningFailure> HashSha256Sync(byte[] data)
    {
        if (IsDisposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (data == null || data.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.MessageRequired());

        try
        {
            unsafe
            {
                byte[] hash = new byte[EcliptixConstants.Sha256HashSize];

                fixed (byte* dataPtr = data)
                fixed (byte* hashPtr = hash)
                {
                    EcliptixResult result = EcliptixNativeLibrary.HashSha256(
                        dataPtr, (nuint)data.Length, hashPtr);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RandomBytesGenerationFailed(error));
                    }
                }

                return Result<byte[], SslPinningFailure>.Ok(hash);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RandomBytesGenerationException(ex));
        }
    }

    public Task<Result<byte[], SslPinningFailure>> GenerateRandomAsync(int size, CancellationToken cancellationToken = default)
        => Task.Run(() => GenerateRandomSync(size), cancellationToken);

    private Result<byte[], SslPinningFailure> GenerateRandomSync(int size)
    {
        if (IsDisposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (size <= 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.InvalidKeySize(1));

        try
        {
            unsafe
            {
                byte[] buffer = new byte[size];

                fixed (byte* bufferPtr = buffer)
                {
                    EcliptixResult result = EcliptixNativeLibrary.GenerateRandom(
                        bufferPtr, (nuint)size);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RandomBytesGenerationFailed(error));
                    }
                }

                return Result<byte[], SslPinningFailure>.Ok(buffer);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RandomBytesGenerationException(ex));
        }
    }



    private static unsafe string GetErrorString(EcliptixResult result)
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
            // Swallow and fall back to code
        }

        return $"Error code: {result}";
    }

    public void Dispose()
    {
        // Make dispose idempotent
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            if (IsInitialized)
            {
                EcliptixNativeLibrary.Cleanup();
            }
        }
        catch
        {
            // Never throw from Dispose
        }
        finally
        {
            Volatile.Write(ref _initialized, 0);
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
