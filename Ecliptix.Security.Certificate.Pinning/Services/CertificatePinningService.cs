using Ecliptix.Pinning.Agent;
using Ecliptix.Utilities.Failures.CertificatePinning;

namespace Ecliptix.Security.Certificate.Pinning.Services;

public sealed class CertificatePinningService : IAsyncDisposable
{
    private const int NOT_INITIALIZED = 0;
    private const int INITIALIZING = 1;
    private const int INITIALIZED = 2;
    private const int DISPOSED = 3;

    private volatile int _state = NOT_INITIALIZED;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);

    public CertificatePinningOperationResult Initialize(CancellationToken cancellationToken = default)
    {
        if (_state == DISPOSED)
        {
            return CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceDisposed());
        }

        if (_state == INITIALIZED)
        {
            return CertificatePinningOperationResult.Success();
        }

        _initializationLock.Wait(cancellationToken);
        try
        {
            if (_state == INITIALIZED)
            {
                return CertificatePinningOperationResult.Success();
            }

            if (_state == DISPOSED)
            {
                return CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceDisposed());
            }

            Interlocked.Exchange(ref _state, INITIALIZING);

            CertificatePinningOperationResult result = InitializeCore();

            Interlocked.Exchange(ref _state, result.IsSuccess ? INITIALIZED : NOT_INITIALIZED);
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
            CryptoClient.Initialize();
            return CertificatePinningOperationResult.Success();
        }
        catch (CryptoException ex)
        {
            return CertificatePinningOperationResult.FromError(
                CertificatePinningFailure.LibraryInitializationFailed(ex.Message));
        }
        catch (Exception ex)
        {
            return CertificatePinningOperationResult.FromError(
                CertificatePinningFailure.InitializationExceptionOccurred(ex));
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

        return VerifySignatureCore(data.Span, signature.Span);
    }

    private static CertificatePinningBoolResult VerifySignatureCore(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            bool isValid = CryptoClient.Verify(data, signature);
            return CertificatePinningBoolResult.FromValue(isValid);
        }
        catch (CryptoException ex)
        {
            return CertificatePinningBoolResult.FromError(
                CertificatePinningFailure.Ed25519VerificationError(ex.Message));
        }
        catch (Exception ex)
        {
            return CertificatePinningBoolResult.FromError(
                CertificatePinningFailure.Ed25519VerificationExceptionOccurred(ex));
        }
    }

    public CertificatePinningByteArrayResult Encrypt(ReadOnlyMemory<byte> plaintext)
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

        return EncryptCore(plaintext.Span);
    }

    private static CertificatePinningByteArrayResult EncryptCore(ReadOnlySpan<byte> plaintext)
    {
        try
        {
            byte[] ciphertext = CryptoClient.Encrypt(plaintext);
            return CertificatePinningByteArrayResult.FromValue(ciphertext);
        }
        catch (CryptoException ex)
        {
            return CertificatePinningByteArrayResult.FromError(
                CertificatePinningFailure.RsaEncryptionFailed(ex.Message));
        }
        catch (Exception ex)
        {
            return CertificatePinningByteArrayResult.FromError(
                CertificatePinningFailure.RsaEncryptionExceptionOccurred(ex));
        }
    }

    public CertificatePinningByteArrayResult Decrypt(ReadOnlyMemory<byte> ciphertext)
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

        return DecryptCore(ciphertext.Span);
    }

    private static CertificatePinningByteArrayResult DecryptCore(ReadOnlySpan<byte> ciphertext)
    {
        try
        {
            byte[] plaintext = CryptoClient.Decrypt(ciphertext);
            return CertificatePinningByteArrayResult.FromValue(plaintext);
        }
        catch (CryptoException ex)
        {
            return CertificatePinningByteArrayResult.FromError(
                CertificatePinningFailure.RsaDecryptionFailed(ex.Message));
        }
        catch (Exception ex)
        {
            return CertificatePinningByteArrayResult.FromError(
                CertificatePinningFailure.RsaDecryptionExceptionOccurred(ex));
        }
    }

    public CertificatePinningByteArrayResult GetPublicKey()
    {
        CertificatePinningOperationResult stateCheck = ValidateOperationState();
        if (!stateCheck.IsSuccess)
        {
            return CertificatePinningByteArrayResult.FromError(stateCheck.Error!);
        }

        return GetPublicKeyCore();
    }

    private static CertificatePinningByteArrayResult GetPublicKeyCore()
    {
        try
        {
            byte[] publicKey = CryptoClient.GetPublicKey();
            return CertificatePinningByteArrayResult.FromValue(publicKey);
        }
        catch (CryptoException ex)
        {
            return CertificatePinningByteArrayResult.FromError(
                CertificatePinningFailure.CertificateValidationFailed(ex.Message));
        }
        catch (Exception ex)
        {
            return CertificatePinningByteArrayResult.FromError(
                CertificatePinningFailure.CertificateValidationExceptionOccurred(ex));
        }
    }

    private CertificatePinningOperationResult ValidateOperationState()
    {
        return _state switch
        {
            DISPOSED => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceDisposed()),
            NOT_INITIALIZED => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceNotInitialized()),
            INITIALIZING => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceInitializing()),
            INITIALIZED => CertificatePinningOperationResult.Success(),
            _ => CertificatePinningOperationResult.FromError(CertificatePinningFailure.ServiceInvalidState())
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _state, DISPOSED) == DISPOSED)
        {
            return;
        }

        try
        {
            await Task.Run(static () =>
            {
                try
                {
                    CryptoClient.Cleanup();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[CERTIFICATE-PINNING] Native cleanup failed during disposal");
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _initializationLock.Dispose();
        }
    }
}
