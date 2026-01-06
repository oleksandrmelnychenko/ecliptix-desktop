using System.Runtime.InteropServices;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Native = Ecliptix.Protocol.System.Native.NativeInterop;

namespace Ecliptix.Protocol.System.Native;

public sealed class EcliptixProtocolSystemWrapper : IDisposable
{
    private IntPtr _handle;
    private readonly EcliptixIdentityKeysWrapper _identityKeys;
    private bool _disposed;
    private GCHandle _callbackHandle;
    private Native.EcliptixCallbacks _callbacks;

    public static Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure> Create(
        EcliptixIdentityKeysWrapper identityKeys)
    {
        if (identityKeys.IsDisposed)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_create(
            identityKeys.Handle,
            out IntPtr handle,
            out Native.EcliptixError error);

        if (result == Native.EcliptixErrorCode.Success)
        {
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
                new EcliptixProtocolSystemWrapper(handle, identityKeys));
        }

        string errorMessage = error.GetMessage();
        Native.ecliptix_error_free(ref error);
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

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_create_from_root(
            identityKeys.Handle,
            rootKey,
            (nuint)rootKey.Length,
            peerBundle,
            (nuint)peerBundle.Length,
            isInitiator,
            out IntPtr handle,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
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
            Native.EcliptixProtocolEventCallback callback = (connectionId, _) =>
            {
                onProtocolStateChanged(connectionId);
            };

            _callbackHandle = GCHandle.Alloc(callback);

            _callbacks = new Native.EcliptixCallbacks
            {
                OnProtocolStateChanged = callback,
                UserData = IntPtr.Zero
            };

            Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_set_callbacks(
                _handle,
                in _callbacks,
                out Native.EcliptixError error);

            if (result != Native.EcliptixErrorCode.Success)
            {
                string errorMessage = error.GetMessage();
                Native.ecliptix_error_free(ref error);

                _callbackHandle.Free();
                throw new InvalidOperationException($"Failed to set callbacks: {errorMessage}");
            }
        }
        else
        {
            _callbacks = new Native.EcliptixCallbacks
            {
                OnProtocolStateChanged = null,
                UserData = IntPtr.Zero
            };

            Native.ecliptix_protocol_system_set_callbacks(
                _handle,
                in _callbacks,
                out _);
        }
    }

    public Result<byte[], EcliptixProtocolFailure> SendMessage(byte[] plaintext)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = Native.ecliptix_buffer_allocate(0);
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_send_message(
            _handle,
            plaintext,
            (nuint)plaintext.Length,
            bufferPtr,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            Native.ecliptix_buffer_free(bufferPtr);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public Result<byte[], EcliptixProtocolFailure> ReceiveMessage(byte[] encryptedEnvelope)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = Native.ecliptix_buffer_allocate(0);
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_receive_message(
            _handle,
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            bufferPtr,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            Native.ecliptix_buffer_free(bufferPtr);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public EcliptixIdentityKeysWrapper GetIdentityKeys() => _identityKeys;

    public Result<byte[], EcliptixProtocolFailure> BeginHandshake(uint connectionId, byte exchangeType)
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = Native.ecliptix_buffer_allocate(0);
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_begin_handshake(
            _handle,
            connectionId,
            exchangeType,
            bufferPtr,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            Native.ecliptix_buffer_free(bufferPtr);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    /// <summary>
    /// Begins handshake with encapsulation to peer's Kyber public key.
    /// Use this when you have the peer's Kyber key (e.g., from their bundle).
    /// The resulting handshake message will include kyber_ciphertext for peer to decapsulate.
    /// </summary>
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

        IntPtr bufferPtr = Native.ecliptix_buffer_allocate(0);
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_begin_handshake_with_peer_kyber(
            _handle,
            connectionId,
            exchangeType,
            peerKyberPublicKey,
            (nuint)peerKyberPublicKey.Length,
            bufferPtr,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            Native.ecliptix_buffer_free(bufferPtr);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return CopyAndFree(bufferPtr);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteHandshake(byte[] peerHandshakeMessage, byte[] rootKey)
    {
        ThrowIfDisposed();

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_complete_handshake(
            _handle,
            peerHandshakeMessage,
            (nuint)peerHandshakeMessage.Length,
            rootKey,
            (nuint)rootKey.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteHandshakeAuto(byte[] peerHandshakeMessage)
    {
        ThrowIfDisposed();

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_complete_handshake_auto(
            _handle,
            peerHandshakeMessage,
            (nuint)peerHandshakeMessage.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<bool, EcliptixProtocolFailure> HasConnection()
    {
        ThrowIfDisposed();
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_has_connection(
            _handle,
            out bool hasConn,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<bool, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<bool, EcliptixProtocolFailure>.Ok(hasConn);
    }

    public Result<uint, EcliptixProtocolFailure> GetConnectionId()
    {
        ThrowIfDisposed();
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_get_connection_id(
            _handle,
            out uint id,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<uint, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<uint, EcliptixProtocolFailure>.Ok(id);
    }

    public Result<uint?, EcliptixProtocolFailure> GetSelectedOpkId()
    {
        ThrowIfDisposed();
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_get_selected_opk_id(
            _handle,
            out bool hasOpkId,
            out uint opkId,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<uint?, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<uint?, EcliptixProtocolFailure>.Ok(hasOpkId ? opkId : null);
    }

    public Result<(uint SendingIndex, uint ReceivingIndex), EcliptixProtocolFailure> GetChainIndices()
    {
        ThrowIfDisposed();

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_get_chain_indices(
            _handle,
            out uint sendingIndex,
            out uint receivingIndex,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<(uint, uint), EcliptixProtocolFailure>.Err(ConvertError(result, errorMessage));
        }

        return Result<(uint, uint), EcliptixProtocolFailure>.Ok((sendingIndex, receivingIndex));
    }

    /// <summary>
    /// Returns the session age in seconds since creation.
    /// Application layer can use this to decide when to refresh/rehandshake.
    /// </summary>
    public Result<ulong, EcliptixProtocolFailure> GetSessionAgeSeconds()
    {
        ThrowIfDisposed();

        Native.EcliptixErrorCode result = Native.ecliptix_connection_get_session_age_seconds(
            _handle,
            out ulong ageSeconds,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<ulong, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<ulong, EcliptixProtocolFailure>.Ok(ageSeconds);
    }

    /// <summary>
    /// Set Kyber hybrid handshake secrets on the active connection (manual PQ setup).
    /// </summary>
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

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_set_kyber_secrets(
            _handle,
            kyberCiphertext,
            (nuint)kyberCiphertext.Length,
            kyberSharedSecret,
            (nuint)kyberSharedSecret.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public Result<byte[], EcliptixProtocolFailure> ExportState()
    {
        ThrowIfDisposed();

        IntPtr bufferPtr = Native.ecliptix_buffer_allocate(0);
        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_export_state(
            _handle,
            bufferPtr,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            Native.ecliptix_buffer_free(bufferPtr);
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

        Native.EcliptixErrorCode result = Native.ecliptix_protocol_system_import_state(
            identityKeys.Handle,
            stateBytes,
            (nuint)stateBytes.Length,
            out IntPtr handle,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<EcliptixProtocolSystemWrapper, EcliptixProtocolFailure>.Ok(
            new EcliptixProtocolSystemWrapper(handle, identityKeys));
    }

    public static Result<Unit, EcliptixProtocolFailure> ValidateEnvelopeHybridRequirements(byte[] encryptedEnvelope)
    {
        Native.EcliptixErrorCode result = Native.ecliptix_envelope_validate_hybrid_requirements(
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
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
        Native.EcliptixErrorCode result = Native.ecliptix_derive_root_from_opaque_session_key(
            opaqueSessionKey,
            (nuint)opaqueSessionKey.Length,
            userContext,
            (nuint)userContext.Length,
            rootKey,
            (nuint)rootKey.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            Native.ecliptix_buffer_free(bufferPtr);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                ConvertError(result, errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(rootKey);
    }

    private static Result<byte[], EcliptixProtocolFailure> CopyAndFree(IntPtr bufferPtr)
    {
        try
        {
            Native.EcliptixBuffer buffer = Marshal.PtrToStructure<Native.EcliptixBuffer>(bufferPtr);
            byte[] data = new byte[buffer.Length];
            Marshal.Copy(buffer.Data, data, 0, (int)buffer.Length);
            return Result<byte[], EcliptixProtocolFailure>.Ok(data);
        }
        finally
        {
            if (bufferPtr != IntPtr.Zero)
            {
                Native.ecliptix_buffer_free(bufferPtr);
            }
        }
    }

    private static EcliptixProtocolFailure ConvertError(Native.EcliptixErrorCode code, string message)
    {
        return code switch
        {
            Native.EcliptixErrorCode.ErrorInvalidInput => EcliptixProtocolFailure.InvalidInput(message),
            Native.EcliptixErrorCode.ErrorKeyGeneration => EcliptixProtocolFailure.KeyGeneration(message),
            Native.EcliptixErrorCode.ErrorDeriveKey => EcliptixProtocolFailure.DeriveKey(message),
            Native.EcliptixErrorCode.ErrorHandshake => EcliptixProtocolFailure.Handshake(message),
            Native.EcliptixErrorCode.ErrorEncryption => EcliptixProtocolFailure.Generic(message),
            Native.EcliptixErrorCode.ErrorDecryption => EcliptixProtocolFailure.Generic(message),
            Native.EcliptixErrorCode.ErrorDecode => EcliptixProtocolFailure.Decode(message),
            Native.EcliptixErrorCode.ErrorEncode => EcliptixProtocolFailure.Decode(message),
            Native.EcliptixErrorCode.ErrorPqMissing => EcliptixProtocolFailure.Decode(message),
            Native.EcliptixErrorCode.ErrorBufferTooSmall => EcliptixProtocolFailure.BUFFER_TOO_SMALL(message),
            Native.EcliptixErrorCode.ErrorObjectDisposed => EcliptixProtocolFailure.OBJECT_DISPOSED(message),
            Native.EcliptixErrorCode.ErrorPrepareLocal => EcliptixProtocolFailure.PrepareLocal(message),
            Native.EcliptixErrorCode.ErrorOutOfMemory => EcliptixProtocolFailure.Generic(message),
            Native.EcliptixErrorCode.ErrorNullPointer => EcliptixProtocolFailure.InvalidInput(message),
            Native.EcliptixErrorCode.ErrorInvalidState => EcliptixProtocolFailure.InvalidInput(message),
            Native.EcliptixErrorCode.ErrorReplayAttack => EcliptixProtocolFailure.ReplayAttempt(message),
            Native.EcliptixErrorCode.ErrorSessionExpired => EcliptixProtocolFailure.Generic(message),
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
            Native.ecliptix_protocol_system_destroy(_handle);
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
        Native.EcliptixErrorCode result = Native.ecliptix_identity_keys_create(
            out IntPtr handle,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
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

        Native.EcliptixErrorCode result = Native.ecliptix_identity_keys_create_from_seed(
            seed,
            (nuint)seed.Length,
            out IntPtr handle,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
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

        Native.EcliptixErrorCode result = Native.ecliptix_identity_keys_create_from_seed_with_context(
            seed,
            (nuint)seed.Length,
            accountId,
            (nuint)accountBytes.Length,
            out IntPtr handle,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
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
        Native.EcliptixErrorCode result = Native.ecliptix_identity_keys_get_public_x25519(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicEd25519()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[32];
        Native.EcliptixErrorCode result = Native.ecliptix_identity_keys_get_public_ed25519(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.KeyGeneration(errorMessage));
        }

        return Result<byte[], EcliptixProtocolFailure>.Ok(publicKey);
    }

    public Result<byte[], EcliptixProtocolFailure> GetPublicKyber()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[1184];
        Native.EcliptixErrorCode result = Native.ecliptix_identity_keys_get_public_kyber(
            _handle,
            publicKey,
            (nuint)publicKey.Length,
            out Native.EcliptixError error);

        if (result != Native.EcliptixErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            Native.ecliptix_error_free(ref error);
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
            Native.ecliptix_identity_keys_destroy(_handle);
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
