using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Security.SSL.Native.Native;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.SslPinning;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Security.SSL.Native.Services;

public sealed class SslPinningService : IDisposable, IAsyncDisposable
{
    private int _initialized;
    private int _disposed;

    private const int RsaKeySizeBytes = 256;
    private const int RsaMaxPlaintextSize = 214;

    private const int Ed25519SignatureSize = EcliptixConstants.Ed25519SignatureSize;

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

    public Task<Result<SodiumSecureMemoryHandle, SslPinningFailure>> EncryptRsaSecureAsync(byte[] plaintext, CancellationToken cancellationToken = default)
        => Task.Run(() => EncryptRsaSecureSync(plaintext), cancellationToken);

    private Result<SodiumSecureMemoryHandle, SslPinningFailure> EncryptRsaSecureSync(byte[] plaintext)
    {
        if (IsDisposed)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (plaintext.Length == 0)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.PlaintextRequired());

        if (plaintext.Length > RsaMaxPlaintextSize)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.PlaintextTooLarge());

        try
        {
            unsafe
            {
                byte[] tempCiphertext = new byte[RsaKeySizeBytes];
                try
                {
                    nuint ciphertextSize = RsaKeySizeBytes;

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

                    Result<SodiumSecureMemoryHandle, SodiumFailure> handleResult =
                        SodiumSecureMemoryHandle.Allocate((int)ciphertextSize);

                    if (handleResult.IsErr)
                        return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(
                            SslPinningFailure.SecureMemoryAllocationFailed(handleResult.UnwrapErr().Message));

                    SodiumSecureMemoryHandle handle = handleResult.Unwrap();

                    Result<Unit, SodiumFailure> writeResult =
                        handle.Write(tempCiphertext.AsSpan(0, (int)ciphertextSize));

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

    public Task<Result<byte[], SslPinningFailure>> DecryptRsaAsync(byte[] ciphertext, byte[] privateKeyPem, CancellationToken cancellationToken = default)
        => Task.Run(() => DecryptRsaSync(ciphertext, privateKeyPem), cancellationToken);

    public Task<Result<SodiumSecureMemoryHandle, SslPinningFailure>> DecryptRsaSecureAsync(SodiumSecureMemoryHandle ciphertextHandle, SodiumSecureMemoryHandle privateKeyHandle, CancellationToken cancellationToken = default)
        => Task.Run(() => DecryptRsaSecureSync(ciphertextHandle, privateKeyHandle), cancellationToken);

    private Result<byte[], SslPinningFailure> DecryptRsaSync(byte[] ciphertext, byte[] privateKeyPem)
    {
        if (IsDisposed)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (ciphertext == null || ciphertext.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.CiphertextRequired());

        if (privateKeyPem == null || privateKeyPem.Length == 0)
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.PrivateKeyRequired());

        try
        {
            unsafe
            {
                byte[] plaintext = new byte[RsaMaxPlaintextSize];
                nuint plaintextSize = (nuint)RsaMaxPlaintextSize;

                fixed (byte* ciphertextPtr = ciphertext)
                fixed (byte* privateKeyPtr = privateKeyPem)
                fixed (byte* plaintextPtr = plaintext)
                {
                    EcliptixResult result = EcliptixNativeLibrary.DecryptRSA(
                        ciphertextPtr, (nuint)ciphertext.Length,
                        privateKeyPtr, (nuint)privateKeyPem.Length,
                        plaintextPtr, &plaintextSize);

                    if (result != EcliptixResult.Success)
                    {
                        string error = GetErrorString(result);
                        return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RsaDecryptionFailed(error));
                    }
                }

                if ((int)plaintextSize < plaintext.Length)
                {
                    Array.Resize(ref plaintext, (int)plaintextSize);
                }

                return Result<byte[], SslPinningFailure>.Ok(plaintext);
            }
        }
        catch (Exception ex)
        {
            return Result<byte[], SslPinningFailure>.Err(SslPinningFailure.RsaDecryptionException(ex));
        }
    }

    private Result<SodiumSecureMemoryHandle, SslPinningFailure> DecryptRsaSecureSync(SodiumSecureMemoryHandle ciphertextHandle, SodiumSecureMemoryHandle privateKeyHandle)
    {
        if (IsDisposed)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (ciphertextHandle == null || ciphertextHandle.IsInvalid)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.CiphertextRequired());

        if (privateKeyHandle == null || privateKeyHandle.IsInvalid)
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.PrivateKeyRequired());

        try
        {
            byte[] tempCiphertext = new byte[ciphertextHandle.Length];
            byte[] tempPrivateKey = new byte[privateKeyHandle.Length];

            try
            {
                Result<Unit, SodiumFailure> ciphertextReadResult = ciphertextHandle.Read(tempCiphertext);
                if (ciphertextReadResult.IsErr)
                    return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(
                        SslPinningFailure.SecureMemoryReadFailed(ciphertextReadResult.UnwrapErr().Message));

                Result<Unit, SodiumFailure> privateKeyReadResult = privateKeyHandle.Read(tempPrivateKey);
                if (privateKeyReadResult.IsErr)
                    return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(
                        SslPinningFailure.SecureMemoryReadFailed(privateKeyReadResult.UnwrapErr().Message));

                unsafe
                {
                    byte[] tempPlaintext = new byte[RsaMaxPlaintextSize];
                    try
                    {
                        nuint plaintextSize = (nuint)RsaMaxPlaintextSize;

                        fixed (byte* ciphertextPtr = tempCiphertext)
                        fixed (byte* privateKeyPtr = tempPrivateKey)
                        fixed (byte* plaintextPtr = tempPlaintext)
                        {
                            EcliptixResult nativeResult = EcliptixNativeLibrary.DecryptRSA(
                                ciphertextPtr, (nuint)tempCiphertext.Length,
                                privateKeyPtr, (nuint)tempPrivateKey.Length,
                                plaintextPtr, &plaintextSize);

                            if (nativeResult != EcliptixResult.Success)
                            {
                                string error = GetErrorString(nativeResult);
                                return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.RsaDecryptionFailed(error));
                            }
                        }

                        Result<SodiumSecureMemoryHandle, SodiumFailure> handleResult =
                            SodiumSecureMemoryHandle.Allocate((int)plaintextSize);

                        if (handleResult.IsErr)
                            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(
                                SslPinningFailure.SecureMemoryAllocationFailed(handleResult.UnwrapErr().Message));

                        SodiumSecureMemoryHandle handle = handleResult.Unwrap();

                        Result<Unit, SodiumFailure> writeResult =
                            handle.Write(tempPlaintext.AsSpan(0, (int)plaintextSize));

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
                        CryptographicOperations.ZeroMemory(tempPlaintext);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(tempCiphertext);
                CryptographicOperations.ZeroMemory(tempPrivateKey);
            }
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, SslPinningFailure>.Err(SslPinningFailure.RsaDecryptionException(ex));
        }
    }

    public Task<Result<bool, SslPinningFailure>> VerifyDigitalSignatureAsync(byte[] message, byte[] signature, CancellationToken cancellationToken = default)
        => Task.Run(() => VerifyDigitalSignatureSync(message, signature), cancellationToken);

    private Result<bool, SslPinningFailure> VerifyDigitalSignatureSync(byte[] message, byte[] signature)
    {
        if (IsDisposed)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceDisposed());

        if (!IsInitialized)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.ServiceNotInitialized());

        if (message.Length == 0)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.MessageRequired());

        if (signature.Length != Ed25519SignatureSize)
            return Result<bool, SslPinningFailure>.Err(SslPinningFailure.InvalidSignatureSize(Ed25519SignatureSize));

        try
        {
            unsafe
            {
                fixed (byte* messagePtr = message)
                fixed (byte* sigPtr = signature)
                {
                    EcliptixResult result = EcliptixNativeLibrary.VerifyDigitalSignature(
                        messagePtr, (nuint)message.Length, sigPtr);

                    if (result == EcliptixResult.Success)
                        return Result<bool, SslPinningFailure>.Ok(true);

                    if (result == EcliptixResult.ErrorVerificationFailed)
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
