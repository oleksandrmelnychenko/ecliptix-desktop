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
    private NativeInterop.EcliptixCallbacks _callbacks;

    public static Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> Create(
        EcliptixIdentityKeysWrapper identityKeys)
    {
        if (identityKeys.IsDisposed)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_create(
            identityKeys.Handle,
            out IntPtr handle,
            out NativeInterop.EcliptixError error);

        if (result == NativeInterop.EcliptixErrorCode.Success)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
                new EcliptixProtocolSystemWrapper(handle, identityKeys));
        }

        string errorMessage = error.GetMessage();
        NativeInterop.ecliptix_error_free(ref error);
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

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_create_from_root(
            identityKeys.Handle,
            rootKey,
            (nuint)rootKey.Length,
            peerBundle,
            (nuint)peerBundle.Length,
            isInitiator,
            out IntPtr handle,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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
            NativeInterop.EcliptixProtocolEventCallback callback = (connectionId, _) =>
            {
                onProtocolStateChanged(connectionId);
            };

            _callbackHandle = GCHandle.Alloc(callback);

            _callbacks = new NativeInterop.EcliptixCallbacks
            {
                OnProtocolStateChanged = callback,
                UserData = IntPtr.Zero
            };

            NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_set_callbacks(
                _handle,
                in _callbacks,
                out NativeInterop.EcliptixError error);

            if (result != NativeInterop.EcliptixErrorCode.Success)
            {
                string errorMessage = error.GetMessage();
                NativeInterop.ecliptix_error_free(ref error);

                _callbackHandle.Free();
                throw new InvalidOperationException($"Failed to set callbacks: {errorMessage}");
            }
        }
        else
        {
            _callbacks = new NativeInterop.EcliptixCallbacks
            {
                OnProtocolStateChanged = null,
                UserData = IntPtr.Zero
            };

            NativeInterop.ecliptix_protocol_system_set_callbacks(
                _handle,
                in _callbacks,
                out _);
        }
    }

    public Result<byte[], EcliptixProtocolFailure> SendMessage(byte[] plaintext)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = NativeInterop.ecliptix_buffer_allocate(0);
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_send_message(
            _handle,
            plaintext,
            (nuint)plaintext.Length,
            bufferPtr,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public Result<byte[], EcliptixProtocolFailure> ReceiveMessage(byte[] encryptedEnvelope)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = NativeInterop.ecliptix_buffer_allocate(0);
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_receive_message(
            _handle,
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            bufferPtr,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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

        IntPtr bufferPtr = NativeInterop.ecliptix_buffer_allocate(0);
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_begin_handshake_with_peer_kyber(
            _handle,
            connectionId,
            exchangeType,
            peerKyberPublicKey,
            (nuint)peerKyberPublicKey.Length,
            bufferPtr,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteHandshake(byte[] peerHandshakeMessage, byte[] rootKey)
    {
        ThrowIfDisposed();

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_complete_handshake(
            _handle,
            peerHandshakeMessage,
            (nuint)peerHandshakeMessage.Length,
            rootKey,
            (nuint)rootKey.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteHandshakeAuto(byte[] peerHandshakeMessage)
    {
        ThrowIfDisposed();

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_complete_handshake_auto(
            _handle,
            peerHandshakeMessage,
            (nuint)peerHandshakeMessage.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<bool, EcliptixProtocolFailure> HasConnection()
    {
        ThrowIfDisposed();
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_has_connection(
            _handle,
            out bool hasConn,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<bool, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<bool, EcliptixProtocolFailure>.Ok(hasConn);
    }

    public Result<uint, EcliptixProtocolFailure> GetConnectionId()
    {
        ThrowIfDisposed();
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_get_connection_id(
            _handle,
            out uint id,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<uint, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<uint, EcliptixProtocolFailure>.Ok(id);
    }

    public Result<uint?, EcliptixProtocolFailure> GetSelectedOpkId()
    {
        ThrowIfDisposed();
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_get_selected_opk_id(
            _handle,
            out bool hasOpkId,
            out uint opkId,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<uint?, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<uint?, EcliptixProtocolFailure>.Ok(hasOpkId ? opkId : null);
    }

    public Result<(uint SendingIndex, uint ReceivingIndex), EcliptixProtocolFailure> GetChainIndices()
    {
        ThrowIfDisposed();

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_get_chain_indices(
            _handle,
            out uint sendingIndex,
            out uint receivingIndex,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<(uint, uint), EcliptixProtocolFailure>.Err(ConvertError(result, errorMessage));
        }

        return Result<(uint, uint), EcliptixProtocolFailure>.Ok((sendingIndex, receivingIndex));
    }

    public Result<ulong, EcliptixProtocolFailure> GetSessionAgeSeconds()
    {
        ThrowIfDisposed();

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_connection_get_session_age_seconds(
            _handle,
            out ulong ageSeconds,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_set_kyber_secrets(
            _handle,
            kyberCiphertext,
            (nuint)kyberCiphertext.Length,
            kyberSharedSecret,
            (nuint)kyberSharedSecret.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<byte[], EcliptixProtocolFailure> ExportState()
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = NativeInterop.ecliptix_buffer_allocate(0);
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_export_state(
            _handle,
            bufferPtr,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            NativeInterop.ecliptix_buffer_free(bufferPtr);
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

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_protocol_system_import_state(
            identityKeys.Handle,
            stateBytes,
            (nuint)stateBytes.Length,
            out IntPtr handle,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixProtocolSystemWrapper(handle, identityKeys));
    }

    public static Result<Unit, EcliptixProtocolFailure> ValidateEnvelopeHybridRequirements(byte[] encryptedEnvelope)
    {
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_envelope_validate_hybrid_requirements(
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public static Result<byte[], EcliptixProtocolFailure> DeriveRootFromOpaqueSessionKey(
        byte[] opaqueSessionKey,
        byte[] userContext)
    {
        if (userContext.Length == 0)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("OPAQUE user context is missing"));
        }

        byte[] rootKey = new byte[32];
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_derive_root_from_opaque_session_key(
            opaqueSessionKey,
            (nuint)opaqueSessionKey.Length,
            userContext,
            (nuint)userContext.Length,
            rootKey,
            (nuint)rootKey.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(rootKey);
    }

    private static Result<byte[], EcliptixProtocolFailure> CopyAndFree(IntPtr bufferPtr)
    {
        try
        {
            NativeInterop.EcliptixBuffer buffer = Marshal.PtrToStructure<NativeInterop.EcliptixBuffer>(bufferPtr);
            byte[] data = new byte[buffer.Length];
            Marshal.Copy(buffer.Data, data, 0, (int)buffer.Length);
            return Result<byte[], EcliptixProtocolFailure>.Ok(data);
        }
        finally
        {
            if (bufferPtr != IntPtr.Zero)
            {
                NativeInterop.ecliptix_buffer_free(bufferPtr);
            }
        }
    }

    private static EcliptixProtocolFailure ConvertError(NativeInterop.EcliptixErrorCode code, string message)
    {
        return code switch
        {
            NativeInterop.EcliptixErrorCode.ErrorInvalidInput => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EcliptixErrorCode.ErrorKeyGeneration => EcliptixProtocolFailure.KeyGeneration(message),
            NativeInterop.EcliptixErrorCode.ErrorDeriveKey => EcliptixProtocolFailure.DeriveKey(message),
            NativeInterop.EcliptixErrorCode.ErrorHandshake => EcliptixProtocolFailure.Handshake(message),
            NativeInterop.EcliptixErrorCode.ErrorEncryption => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EcliptixErrorCode.ErrorDecryption => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EcliptixErrorCode.ErrorDecode => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EcliptixErrorCode.ErrorEncode => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EcliptixErrorCode.ErrorPqMissing => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EcliptixErrorCode.ErrorBufferTooSmall => EcliptixProtocolFailure.BUFFER_TOO_SMALL(message),
            NativeInterop.EcliptixErrorCode.ErrorObjectDisposed => EcliptixProtocolFailure.OBJECT_DISPOSED(message),
            NativeInterop.EcliptixErrorCode.ErrorPrepareLocal => EcliptixProtocolFailure.PrepareLocal(message),
            NativeInterop.EcliptixErrorCode.ErrorOutOfMemory => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EcliptixErrorCode.ErrorNullPointer => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EcliptixErrorCode.ErrorInvalidState => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EcliptixErrorCode.ErrorReplayAttack => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EcliptixErrorCode.ErrorSessionExpired => EcliptixProtocolFailure.SessionExpired(message),
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
            NativeInterop.ecliptix_protocol_system_destroy(_handle);
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
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_identity_keys_create(
            out IntPtr handle,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_identity_keys_create_from_seed(
            seed,
            (nuint)seed.Length,
            out IntPtr handle,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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

        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_identity_keys_create_from_seed_with_context(
            seed,
            (nuint)seed.Length,
            accountId,
            (nuint)accountBytes.Length,
            out IntPtr handle,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_identity_keys_get_public_x25519(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicEd25519()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[32];
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_identity_keys_get_public_ed25519(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicKyber()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[1184];
        NativeInterop.EcliptixErrorCode result = NativeInterop.ecliptix_identity_keys_get_public_kyber(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out NativeInterop.EcliptixError error);

        if (result != NativeInterop.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.ecliptix_error_free(ref error);
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
            NativeInterop.ecliptix_identity_keys_destroy(_handle);
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
