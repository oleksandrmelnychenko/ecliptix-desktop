using System.Runtime.InteropServices;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protected.Protocol.Native;

public sealed class EcliptixProtocolSystemWrapper : IDisposable
{
    private IntPtr _handle;
    private readonly EcliptixIdentityKeysWrapper _identityKeys;
    private bool _disposed;
    private GCHandle _callbackHandle;
    private NativeInterop.EppCallbacks _callbacks;

    public static Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> Create(
        EcliptixIdentityKeysWrapper identityKeys)
    {
        if (identityKeys.IsDisposed)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_create(
            identityKeys.Handle,
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result == NativeInterop.EppErrorCode.Success)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
                new EcliptixProtocolSystemWrapper(handle, identityKeys));
        }

        string errorMessage = error.GetMessage();
        NativeInterop.epp_error_free(ref error);
        return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
            ConvertError(result, errorMessage));
    }

    public static Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> CreateFromRoot(
        EcliptixIdentityKeysWrapper identityKeys,
        byte[] rootKey,
        byte[] peerBundle,
        bool isInitiator)
    {
        if (identityKeys.IsDisposed)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }
        if (rootKey.Length != 32)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Root key must be 32 bytes"));
        }
        if (peerBundle.Length == 0)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Peer bundle is missing"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_create_from_root(
            identityKeys.Handle,
            rootKey,
            (nuint)rootKey.Length,
            peerBundle,
            (nuint)peerBundle.Length,
            isInitiator,
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixProtocolSystemWrapper(handle, identityKeys));
    }

    private EcliptixProtocolSystemWrapper(IntPtr handle, EcliptixIdentityKeysWrapper identityKeys)
    {
        _handle = handle;
        _identityKeys = identityKeys;
        _disposed = false;
    }

    public void SetEventHandler(Action<uint>? onProtocolStateChanged)
    {
        ThrowIfDisposed();

        if (_callbackHandle.IsAllocated)
        {
            _callbackHandle.Free();
        }

        if (onProtocolStateChanged != null)
        {
            NativeInterop.EppEventCallback callback = (connectionId, _) =>
            {
                onProtocolStateChanged(connectionId);
            };

            _callbackHandle = GCHandle.Alloc(callback);

            _callbacks = new NativeInterop.EppCallbacks
            {
                OnProtocolStateChanged = callback,
                UserData = IntPtr.Zero
            };

            NativeInterop.EppErrorCode result = NativeInterop.epp_session_set_callbacks(
                _handle,
                in _callbacks,
                out NativeInterop.EppError error);

            if (result != NativeInterop.EppErrorCode.Success)
            {
                string errorMessage = error.GetMessage();
                NativeInterop.epp_error_free(ref error);

                _callbackHandle.Free();
                throw new InvalidOperationException($"Failed to set callbacks: {errorMessage}");
            }
        }
        else
        {
            _callbacks = new NativeInterop.EppCallbacks
            {
                OnProtocolStateChanged = null,
                UserData = IntPtr.Zero
            };

            NativeInterop.epp_session_set_callbacks(
                _handle,
                in _callbacks,
                out _);
        }
    }

    public Result<byte[], EcliptixProtocolFailure> SendMessage(byte[] plaintext)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = NativeInterop.epp_buffer_alloc(0);
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_encrypt(
            _handle,
            plaintext,
            (nuint)plaintext.Length,
            bufferPtr,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public Result<byte[], EcliptixProtocolFailure> ReceiveMessage(byte[] encryptedEnvelope)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = NativeInterop.epp_buffer_alloc(0);
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_decrypt(
            _handle,
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            bufferPtr,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public EcliptixIdentityKeysWrapper GetIdentityKeys() => _identityKeys;

    public Result<byte[], EcliptixProtocolFailure> BeginHandshakeWithPeerKyber(
        uint connectionId,
        byte exchangeType,
        byte[] peerKyberPublicKey)
    {
        ThrowIfDisposed();

        if (peerKyberPublicKey == null || peerKyberPublicKey.Length != 1184)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Peer Kyber public key must be 1184 bytes"));
        }

        IntPtr bufferPtr = NativeInterop.epp_buffer_alloc(0);
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_begin_handshake(
            _handle,
            connectionId,
            exchangeType,
            peerKyberPublicKey,
            (nuint)peerKyberPublicKey.Length,
            bufferPtr,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteHandshake(byte[] peerHandshakeMessage, byte[] rootKey)
    {
        ThrowIfDisposed();

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_complete_handshake(
            _handle,
            peerHandshakeMessage,
            (nuint)peerHandshakeMessage.Length,
            rootKey,
            (nuint)rootKey.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteHandshakeAuto(byte[] peerHandshakeMessage)
    {
        ThrowIfDisposed();

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_complete_handshake_auto(
            _handle,
            peerHandshakeMessage,
            (nuint)peerHandshakeMessage.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<bool, EcliptixProtocolFailure> HasConnection()
    {
        ThrowIfDisposed();
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_is_established(
            _handle,
            out bool hasConn,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<bool, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<bool, EcliptixProtocolFailure>.Ok(hasConn);
    }

    public Result<uint, EcliptixProtocolFailure> GetConnectionId()
    {
        ThrowIfDisposed();
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_get_id(
            _handle,
            out uint id,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<uint, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<uint, EcliptixProtocolFailure>.Ok(id);
    }

    public Result<uint?, EcliptixProtocolFailure> GetSelectedOpkId()
    {
        ThrowIfDisposed();
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_get_used_prekey_id(
            _handle,
            out bool hasOpkId,
            out uint opkId,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<uint?, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<uint?, EcliptixProtocolFailure>.Ok(hasOpkId ? opkId : null);
    }

    public Result<(uint SendingIndex, uint ReceivingIndex), EcliptixProtocolFailure> GetChainIndices()
    {
        ThrowIfDisposed();

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_get_chain_indices(
            _handle,
            out uint sendingIndex,
            out uint receivingIndex,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<(uint, uint), EcliptixProtocolFailure>.Err(ConvertError(result, errorMessage));
        }

        return Result<(uint, uint), EcliptixProtocolFailure>.Ok((sendingIndex, receivingIndex));
    }

    public Result<ulong, EcliptixProtocolFailure> GetSessionAgeSeconds()
    {
        ThrowIfDisposed();

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_age_seconds(
            _handle,
            out ulong ageSeconds,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<ulong, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<ulong, EcliptixProtocolFailure>.Ok(ageSeconds);
    }

    public Result<Unit, EcliptixProtocolFailure> SetKyberSecrets(byte[] kyberCiphertext, byte[] kyberSharedSecret)
    {
        ThrowIfDisposed();

        if (kyberCiphertext == null || kyberCiphertext.Length == 0)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Kyber ciphertext is null or empty"));
        }
        if (kyberSharedSecret == null || kyberSharedSecret.Length == 0)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Kyber shared secret is null or empty"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_set_kyber_secrets(
            _handle,
            kyberCiphertext,
            (nuint)kyberCiphertext.Length,
            kyberSharedSecret,
            (nuint)kyberSharedSecret.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<byte[], EcliptixProtocolFailure> ExportState()
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = NativeInterop.epp_buffer_alloc(0);
        NativeInterop.EppErrorCode result = NativeInterop.epp_session_serialize(
            _handle,
            bufferPtr,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            NativeInterop.epp_buffer_free(bufferPtr);
            return Result<byte[], EcliptixProtocolFailure>.Err(ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public static Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> ImportState(
        EcliptixIdentityKeysWrapper identityKeys,
        byte[] stateBytes)
    {
        if (identityKeys.IsDisposed)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }
        if (stateBytes.Length == 0)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("State bytes are missing"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_deserialize(
            identityKeys.Handle,
            stateBytes,
            (nuint)stateBytes.Length,
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixProtocolSystemWrapper(handle, identityKeys));
    }

    public static Result<Unit, EcliptixProtocolFailure> ValidateEnvelopeHybridRequirements(byte[] encryptedEnvelope)
    {
        NativeInterop.EppErrorCode result = NativeInterop.epp_envelope_validate(
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<byte[], EcliptixProtocolFailure> CopyAndFree(IntPtr bufferPtr)
    {
        try
        {
            NativeInterop.EppBuffer buffer = Marshal.PtrToStructure<NativeInterop.EppBuffer>(bufferPtr);
            byte[] data = new byte[buffer.Length];
            Marshal.Copy(buffer.Data, data, 0, (int)buffer.Length);
            return Result<byte[], EcliptixProtocolFailure>.Ok(data);
        }
        finally
        {
            if (bufferPtr != IntPtr.Zero)
            {
                NativeInterop.epp_buffer_free(bufferPtr);
            }
        }
    }

    private static EcliptixProtocolFailure ConvertError(NativeInterop.EppErrorCode code, string message)
    {
        return code switch
        {
            NativeInterop.EppErrorCode.ErrorInvalidInput => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EppErrorCode.ErrorKeyGeneration => EcliptixProtocolFailure.KeyGeneration(message),
            NativeInterop.EppErrorCode.ErrorDeriveKey => EcliptixProtocolFailure.DeriveKey(message),
            NativeInterop.EppErrorCode.ErrorHandshake => EcliptixProtocolFailure.Handshake(message),
            NativeInterop.EppErrorCode.ErrorEncryption => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorDecryption => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorDecode => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EppErrorCode.ErrorEncode => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EppErrorCode.ErrorPqMissing => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EppErrorCode.ErrorBufferTooSmall => EcliptixProtocolFailure.BufferTooSmall(message),
            NativeInterop.EppErrorCode.ErrorObjectDisposed => EcliptixProtocolFailure.ObjectDisposed(message),
            NativeInterop.EppErrorCode.ErrorPrepareLocal => EcliptixProtocolFailure.PrepareLocal(message),
            NativeInterop.EppErrorCode.ErrorOutOfMemory => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorNullPointer => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EppErrorCode.ErrorInvalidState => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EppErrorCode.ErrorReplayAttack => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorSessionExpired => EcliptixProtocolFailure.SessionExpired(message),
            _ => EcliptixProtocolFailure.Generic(message)
        };
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EcliptixProtocolSystemWrapper));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_callbackHandle.IsAllocated)
        {
            _callbackHandle.Free();
        }

        if (_handle != IntPtr.Zero)
        {
            NativeInterop.epp_session_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~EcliptixProtocolSystemWrapper()
    {
        Dispose();
    }
}

public sealed class EcliptixIdentityKeysWrapper : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public IntPtr Handle => _handle;
    public bool IsDisposed => _disposed;

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> Create()
    {
        NativeInterop.EppErrorCode result = NativeInterop.epp_identity_create(
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixIdentityKeysWrapper(handle));
    }

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateFromSeed(byte[] seed)
    {
        if (seed == null)
        {
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Seed is null"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_identity_create_from_seed(
            seed,
            (nuint)seed.Length,
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixIdentityKeysWrapper(handle));
    }

    public static Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> CreateFromSeed(
        byte[] seed,
        string accountId)
    {
        if (seed == null)
        {
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Seed is null"));
        }
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Account id is missing"));
        }

        byte[] accountBytes = global::System.Text.Encoding.UTF8.GetBytes(accountId);

        NativeInterop.EppErrorCode result = NativeInterop.epp_identity_create_with_context(
            seed,
            (nuint)seed.Length,
            accountId,
            (nuint)accountBytes.Length,
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixIdentityKeysWrapper(handle));
    }

    private EcliptixIdentityKeysWrapper(IntPtr handle)
    {
        _handle = handle;
        _disposed = false;
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicX25519()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[32];
        NativeInterop.EppErrorCode result = NativeInterop.epp_identity_get_x25519_public(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicEd25519()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[32];
        NativeInterop.EppErrorCode result = NativeInterop.epp_identity_get_ed25519_public(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicKyber()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[1184];
        NativeInterop.EppErrorCode result = NativeInterop.epp_identity_get_kyber_public(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(EcliptixIdentityKeysWrapper));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            NativeInterop.epp_identity_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~EcliptixIdentityKeysWrapper()
    {
        Dispose();
    }
}
