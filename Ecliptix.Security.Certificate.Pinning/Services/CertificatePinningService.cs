using System.Runtime.InteropServices;
using Ecliptix.Security.Certificate.Pinning.Native;
using Ecliptix.Utilities.Failures.CertificatePinning;

namespace Ecliptix.Security.Certificate.Pinning.Services;

public sealed class CertificatePinningService : IAsyncDisposable
{
    private const int NotInitialized = 0;
    private const int Initializing = 1;
    private const int Initialized = 2;
    private const int Disposed = 3;

    private volatile int _state = NotInitialized;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    public CertificatePinningOperationResult Initialize(CancellationToken cancellationToken = default)
    {
        if (_state == Disposed)
        {
            return CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceDisposed());
        }

        if (_state == Initialized)
        {
            return CertificatePinningOperationResult.Success();
        }

        _initializationLock.Wait(cancellationToken);
        try
        {
            if (_state == Initialized)
            {
                return CertificatePinningOperationResult.Success();
            }

            if (_state == Disposed)
            {
                return CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceDisposed());
            }

            Interlocked.Exchange(ref _state, Initializing);

            CertificatePinningOperationResult result = InitializeCore();

            Interlocked.Exchange(ref _state, result.IsSuccess ? Initialized : NotInitialized);
            return result;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static CertificatePinningOperationResult InitializeCore()
    {
        try
        {
            CertificatePinningNativeResult nativeResult = CertificatePinningNativeLibrary.Initialize();
            if (nativeResult != CertificatePinningNativeResult.Success)
            {
                string error = GetErrorStringStatic(nativeResult);
                return CertificatePinningOperationResult.FromError(
                    CertificatePinningFailure.LibraryInitializationFailed(error));
            }

            return CertificatePinningOperationResult.Success();
        }
        catch (Exception ex)
        {
            return CertificatePinningOperationResult.FromError(
                CertificatePinningFailure.InitializationException(ex));
        }
    }

    public CertificatePinningBoolResult VerifyServerSignature(
        ReadOnlyMemory<byte> data,
        ReadOnlyMemory<byte> signature)
    {
        CertificatePinningOperationResult stateCheck = ValidateOperationState();
        if (!stateCheck.IsSuccess)
        {
            return CertificatePinningBoolResult.FromError(stateCheck.Error!);
        }

        if (data.IsEmpty)
        {
            return CertificatePinningBoolResult.FromError(CertificatePinningFailure.MessageRequired());
        }

        if (signature.IsEmpty)
        {
            return CertificatePinningBoolResult.FromError(CertificatePinningFailure.InvalidSignatureSize(0));
        }

        return VerifySignatureUnsafe(data.Span, signature.Span);
    }


    private static CertificatePinningBoolResult VerifySignatureUnsafe(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            unsafe
            {
                fixed (byte* dataPtr = data)
                fixed (byte* signaturePtr = signature)
                {
                    CertificatePinningNativeResult result = CertificatePinningNativeLibrary.VerifySignature(
                        dataPtr, (nuint)data.Length,
                        signaturePtr, (nuint)signature.Length);

                    return result switch
                    {
                        CertificatePinningNativeResult.Success => CertificatePinningBoolResult.FromValue(true),
                        CertificatePinningNativeResult.ErrorVerificationFailed => CertificatePinningBoolResult.FromValue(false),
                        _ => CertificatePinningBoolResult.FromError(
                            CertificatePinningFailure.Ed25519VerificationError(GetErrorStringStatic(result)))
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return CertificatePinningBoolResult.FromError(CertificatePinningFailure.Ed25519VerificationException(ex));
        }
    }

    public CertificatePinningByteArrayResult Encrypt(
        ReadOnlyMemory<byte> plaintext)
    {
        CertificatePinningOperationResult stateCheck = ValidateOperationState();
        if (!stateCheck.IsSuccess)
        {
            return CertificatePinningByteArrayResult.FromError(stateCheck.Error!);
        }

        if (plaintext.IsEmpty)
        {
            return CertificatePinningByteArrayResult.FromError(CertificatePinningFailure.PlaintextRequired());
        }

        return EncryptUnsafe(plaintext.Span);
    }


    private static CertificatePinningByteArrayResult EncryptUnsafe(ReadOnlySpan<byte> plaintext)
    {
        try
        {
            unsafe
            {
                fixed (byte* plaintextPtr = plaintext)
                {
                    const nuint maxStackSize = 1024;
                    nuint estimatedSize = (nuint)plaintext.Length + 256;

                    if (estimatedSize <= maxStackSize)
                    {
                        byte* stackBuffer = stackalloc byte[(int)estimatedSize];
                        nuint actualSize = estimatedSize;

                        CertificatePinningNativeResult result = CertificatePinningNativeLibrary.Encrypt(
                            plaintextPtr, (nuint)plaintext.Length,
                            stackBuffer, &actualSize);

                        if (result == CertificatePinningNativeResult.Success)
                        {
                            byte[] output = new byte[actualSize];
                            fixed (byte* outputPtr = output)
                            {
                                Buffer.MemoryCopy(stackBuffer, outputPtr, actualSize, actualSize);
                            }
                            return CertificatePinningByteArrayResult.FromValue(output);
                        }

                        return CertificatePinningByteArrayResult.FromError(
                            CertificatePinningFailure.RsaEncryptionFailed(GetErrorStringStatic(result)));
                    }
                    else
                    {
                        byte[] ciphertext = new byte[estimatedSize];
                        nuint actualSize = estimatedSize;

                        fixed (byte* ciphertextPtr = ciphertext)
                        {
                            CertificatePinningNativeResult result = CertificatePinningNativeLibrary.Encrypt(
                                plaintextPtr, (nuint)plaintext.Length,
                                ciphertextPtr, &actualSize);

                            if (result == CertificatePinningNativeResult.Success)
                            {
                                if (actualSize != estimatedSize)
                                {
                                    byte[] resized = new byte[actualSize];
                                    Array.Copy(ciphertext, resized, (int)actualSize);
                                    return CertificatePinningByteArrayResult.FromValue(resized);
                                }
                                return CertificatePinningByteArrayResult.FromValue(ciphertext);
                            }

                            return CertificatePinningByteArrayResult.FromError(
                                CertificatePinningFailure.RsaEncryptionFailed(GetErrorStringStatic(result)));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return CertificatePinningByteArrayResult.FromError(CertificatePinningFailure.RsaEncryptionException(ex));
        }
    }

    public CertificatePinningByteArrayResult Decrypt(
        ReadOnlyMemory<byte> ciphertext)
    {
        CertificatePinningOperationResult stateCheck = ValidateOperationState();
        if (!stateCheck.IsSuccess)
        {
            return CertificatePinningByteArrayResult.FromError(stateCheck.Error!);
        }

        if (ciphertext.IsEmpty)
        {
            return CertificatePinningByteArrayResult.FromError(CertificatePinningFailure.CiphertextRequired());
        }

        return DecryptUnsafe(ciphertext.Span);
    }


    private static CertificatePinningByteArrayResult DecryptUnsafe(ReadOnlySpan<byte> ciphertext)
    {
        try
        {
            unsafe
            {
                fixed (byte* ciphertextPtr = ciphertext)
                {
                    nuint plaintextLen = (nuint)ciphertext.Length;
                    byte[] plaintext = new byte[plaintextLen];

                    fixed (byte* plaintextPtr = plaintext)
                    {
                        CertificatePinningNativeResult result = CertificatePinningNativeLibrary.Decrypt(
                            ciphertextPtr, (nuint)ciphertext.Length,
                            plaintextPtr, &plaintextLen);

                        if (result == CertificatePinningNativeResult.Success)
                        {
                            if (plaintextLen != (nuint)ciphertext.Length)
                            {
                                byte[] resized = new byte[plaintextLen];
                                Array.Copy(plaintext, resized, (int)plaintextLen);
                                return CertificatePinningByteArrayResult.FromValue(resized);
                            }
                            return CertificatePinningByteArrayResult.FromValue(plaintext);
                        }

                        return CertificatePinningByteArrayResult.FromError(
                            CertificatePinningFailure.RsaDecryptionFailed(GetErrorStringStatic(result)));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            return CertificatePinningByteArrayResult.FromError(CertificatePinningFailure.RsaDecryptionException(ex));
        }
    }

    public CertificatePinningByteArrayResult GetPublicKey()
    {
        CertificatePinningOperationResult stateCheck = ValidateOperationState();
        if (!stateCheck.IsSuccess)
        {
            return CertificatePinningByteArrayResult.FromError(stateCheck.Error!);
        }

        return GetPublicKeyUnsafe();
    }


    private static CertificatePinningByteArrayResult GetPublicKeyUnsafe()
    {
        try
        {
            unsafe
            {
                const nuint initialKeyBufferSize = 1024;
                nuint keyLen = initialKeyBufferSize;
                byte[] publicKey = new byte[keyLen];

                fixed (byte* keyPtr = publicKey)
                {
                    CertificatePinningNativeResult result = CertificatePinningNativeLibrary.GetPublicKey(keyPtr, &keyLen);

                    if (result == CertificatePinningNativeResult.Success)
                    {
                        if (keyLen != initialKeyBufferSize)
                        {
                            byte[] resized = new byte[keyLen];
                            Array.Copy(publicKey, resized, (int)keyLen);
                            return CertificatePinningByteArrayResult.FromValue(resized);
                        }
                        return CertificatePinningByteArrayResult.FromValue(publicKey);
                    }

                    return CertificatePinningByteArrayResult.FromError(
                        CertificatePinningFailure.CertificateValidationFailed(GetErrorStringStatic(result)));
                }
            }
        }
        catch (Exception ex)
        {
            return CertificatePinningByteArrayResult.FromError(CertificatePinningFailure.CertificateValidationException(ex));
        }
    }

    private CertificatePinningOperationResult ValidateOperationState()
    {
        return _state switch
        {
            Disposed => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceDisposed()),
            NotInitialized => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceNotInitialized()),
            Initializing => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceInitializing()),
            Initialized => CertificatePinningOperationResult.Success(),
            _ => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceInvalidState())
        };
    }

    private static unsafe string GetErrorStringStatic(CertificatePinningNativeResult result)
    {
        try
        {
            byte* errorPtr = CertificatePinningNativeLibrary.GetErrorMessage();
            if (errorPtr != null)
            {
                return Marshal.PtrToStringUTF8((IntPtr)errorPtr) ?? FormattableString.Invariant($"Unknown error: {result}");
            }
        }
        catch
        {
        }

        return FormattableString.Invariant($"Error code: {result}");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state, Disposed) == Disposed)
        {
            return;
        }

        try
        {
            await Task.Run(static () =>
            {
                try
                {
                    CertificatePinningNativeLibrary.Cleanup();
                }
                catch
                {
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Dispose();
        }
    }
}
