using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Sodium;

namespace Ecliptix.Protocol.System.Core;

internal sealed class EcliptixProtocolConnection : IDisposable
{
    private static readonly TimeSpan SessionTimeout = ProtocolSystemConstants.Timeouts.SessionTimeout;

    private static ReadOnlySpan<byte> InitialSenderChainInfo =>
        Encoding.UTF8.GetBytes(ProtocolSystemConstants.Protocol.INITIAL_SENDER_CHAIN_INFO);

    private static ReadOnlySpan<byte> InitialReceiverChainInfo =>
        Encoding.UTF8.GetBytes(ProtocolSystemConstants.Protocol.INITIAL_RECEIVER_CHAIN_INFO);

    private static ReadOnlySpan<byte> DhRatchetInfo =>
        Encoding.UTF8.GetBytes(ProtocolSystemConstants.Protocol.DH_RATCHET_INFO);

    private readonly Lock _lock = new();
    private readonly ReplayProtection _replayProtection = new();
    private readonly RatchetConfig _ratchetConfig;
    private readonly RatchetRecovery _ratchetRecovery = new();

    private readonly DateTimeOffset _createdAt;
    private readonly uint _id;
    private long _lastRatchetTimeTicks = DateTime.UtcNow.Ticks;
    private volatile bool _isFirstReceivingRatchet;
    private readonly bool _isInitiator;
    private readonly EcliptixProtocolChainStep _sendingStep;
    private SodiumSecureMemoryHandle? _currentSendingDhPrivateKeyHandle;
    private volatile bool _disposed;
    private readonly SodiumSecureMemoryHandle? _initialSendingDhPrivateKeyHandle;
    private long _nonceCounter;
    private LocalPublicKeyBundle? _peerBundle;
    private byte[]? _peerDhPublicKey;
    private readonly SodiumSecureMemoryHandle? _persistentDhPrivateKeyHandle;
    private readonly byte[]? _persistentDhPublicKey;
    private volatile bool _receivedNewDhKey;
    private EcliptixProtocolChainStep? _receivingStep;
    private SodiumSecureMemoryHandle? _rootKeyHandle;
    private SodiumSecureMemoryHandle? _metadataEncryptionKeyHandle;
    private IProtocolEventHandler? _eventHandler;
    private readonly PubKeyExchangeType _exchangeType;

    public PubKeyExchangeType ExchangeType => _exchangeType;

    private EcliptixProtocolConnection(uint id, bool isInitiator, SodiumSecureMemoryHandle initialSendingDh,
        EcliptixProtocolChainStep sendingStep, SodiumSecureMemoryHandle persistentDh, byte[] persistentDhPublic,
        RatchetConfig ratchetConfig, PubKeyExchangeType exchangeType)
    {
        _id = id;
        _isInitiator = isInitiator;
        _initialSendingDhPrivateKeyHandle = initialSendingDh;
        _currentSendingDhPrivateKeyHandle = initialSendingDh;
        _sendingStep = sendingStep;
        _persistentDhPrivateKeyHandle = persistentDh;
        _persistentDhPublicKey = persistentDhPublic;
        _ratchetConfig = ratchetConfig;
        _exchangeType = exchangeType;
        _peerBundle = null;
        _receivingStep = null;
        _rootKeyHandle = null;
        _nonceCounter = ProtocolSystemConstants.Protocol.INITIAL_NONCE_COUNTER;
        _createdAt = DateTimeOffset.UtcNow;
        _peerDhPublicKey = null;
        _receivedNewDhKey = false;
        _disposed = false;
    }

    private EcliptixProtocolConnection(uint id, RatchetState proto, EcliptixProtocolChainStep sendingStep,
        EcliptixProtocolChainStep? receivingStep, SodiumSecureMemoryHandle rootKeyHandle, RatchetConfig ratchetConfig,
        PubKeyExchangeType exchangeType)
    {
        _id = id;
        _isInitiator = proto.IsInitiator;
        _createdAt = proto.CreatedAt.ToDateTimeOffset();
        _nonceCounter = (long)proto.NonceCounter;
        _peerBundle = LocalPublicKeyBundle.FromProtobufExchange(proto.PeerBundle).Unwrap();
        if (!proto.PeerDhPublicKey.IsEmpty)
        {
            SecureByteStringInterop.SecureCopyWithCleanup(proto.PeerDhPublicKey, out byte[] peerDhKey);
            _peerDhPublicKey = peerDhKey;
        }
        else
        {
            _peerDhPublicKey = null;
        }

        _isFirstReceivingRatchet = proto.IsFirstReceivingRatchet;
        _rootKeyHandle = rootKeyHandle;
        _sendingStep = sendingStep;
        _receivingStep = receivingStep;
        _currentSendingDhPrivateKeyHandle = sendingStep.GetDhPrivateKeyHandle();
        _initialSendingDhPrivateKeyHandle = null;
        _persistentDhPrivateKeyHandle = null;
        _persistentDhPublicKey = null;
        _ratchetConfig = ratchetConfig;
        _exchangeType = exchangeType;
        _receivedNewDhKey = false;
        _disposed = false;
        _lock = new Lock();
    }

    public void SetEventHandler(IProtocolEventHandler? handler) => _eventHandler = handler;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public static Result<EcliptixProtocolConnection, EcliptixProtocolFailure> Create(uint connectId, bool isInitiator)
    {
        return Create(connectId, isInitiator, RatchetConfig.Default, PubKeyExchangeType.InitialHandshake);
    }

    public static Result<EcliptixProtocolConnection, EcliptixProtocolFailure> Create(
        uint connectId,
        bool isInitiator,
        RatchetConfig ratchetConfig,
        PubKeyExchangeType exchangeType)
    {
        SodiumSecureMemoryHandle? initialSendingDhPrivateKeyHandle = null;
        byte[]? initialSendingDhPrivateKeyBytes = null;
        EcliptixProtocolChainStep? sendingStep = null;
        SodiumSecureMemoryHandle? persistentDhPrivateKeyHandle = null;

        try
        {
            Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> initialKeysResult =
                SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.INITIAL_SENDING_DH_KEY_PURPOSE);
            if (initialKeysResult.IsErr)
            {
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(initialKeysResult.UnwrapErr());
            }

            (initialSendingDhPrivateKeyHandle, byte[] initialSendingDhPublicKey) = initialKeysResult.Unwrap();

            Result<byte[], SodiumFailure> readBytesResult =
                initialSendingDhPrivateKeyHandle.ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE);
            if (readBytesResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    readBytesResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            initialSendingDhPrivateKeyBytes = readBytesResult.Unwrap();

            Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> persistentKeysResult =
                SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.PERSISTENT_DH_KEY_PURPOSE);
            if (persistentKeysResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle.Dispose();
                WipeIfNotNull(initialSendingDhPrivateKeyBytes);
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(persistentKeysResult
                    .UnwrapErr());
            }

            (persistentDhPrivateKeyHandle, byte[] persistentDhPublicKey) = persistentKeysResult.Unwrap();

            byte[] tempChainKey = new byte[Constants.X_25519_KEY_SIZE];
            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> stepResult =
                EcliptixProtocolChainStep.Create(ChainStepType.Sender, tempChainKey,
                    initialSendingDhPrivateKeyBytes, initialSendingDhPublicKey);
            WipeIfNotNull(tempChainKey);
            WipeIfNotNull(initialSendingDhPrivateKeyBytes);
            initialSendingDhPrivateKeyBytes = null;

            if (stepResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle.Dispose();
                persistentDhPrivateKeyHandle.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(stepResult.UnwrapErr());
            }

            sendingStep = stepResult.Unwrap();
            EcliptixProtocolConnection connection = new(connectId, isInitiator,
                initialSendingDhPrivateKeyHandle, sendingStep, persistentDhPrivateKeyHandle,
                persistentDhPublicKey, ratchetConfig, exchangeType);
            initialSendingDhPrivateKeyHandle = null;
            persistentDhPrivateKeyHandle = null;
            sendingStep = null;
            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Ok(connection);
        }
        catch (Exception ex)
        {
            initialSendingDhPrivateKeyHandle?.Dispose();
            sendingStep?.Dispose();
            persistentDhPrivateKeyHandle?.Dispose();
            WipeIfNotNull(initialSendingDhPrivateKeyBytes);
            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(
                    string.Format(EcliptixProtocolFailureMessages.UNEXPECTED_ERROR_CREATING_SESSION, connectId), ex));
        }
    }

    public Result<RatchetState, EcliptixProtocolFailure> ToProtoState()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolConnection)));
            }

            if (_exchangeType == PubKeyExchangeType.ServerStreaming)
            {
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.SERVER_STREAMING_NOT_PERSISTED));
            }

            try
            {
                Result<ChainStepState, EcliptixProtocolFailure> sendingStepStateResult = _sendingStep.ToProtoState();
                if (sendingStepStateResult.IsErr)
                {
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(sendingStepStateResult.UnwrapErr());
                }

                Result<byte[], SodiumFailure> rootKeyReadResult =
                    _rootKeyHandle!.ReadBytes(Constants.X_25519_KEY_SIZE);
                if (rootKeyReadResult.IsErr)
                {
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(
                        rootKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
                }

                byte[] rootKeyBytes = rootKeyReadResult.Unwrap();

                RatchetState proto = new()
                {
                    IsInitiator = _isInitiator,
                    CreatedAt = Timestamp.FromDateTimeOffset(_createdAt),
                    NonceCounter = (ulong)_nonceCounter,
                    PeerBundle = _peerBundle!.ToProtobufExchange(),
                    PeerDhPublicKey = ByteString.CopyFrom(_peerDhPublicKey ?? []),
                    IsFirstReceivingRatchet = _isFirstReceivingRatchet,
                    RootKey = ByteString.CopyFrom(rootKeyBytes),
                    SendingStep = sendingStepStateResult.Unwrap()
                };

                if (_receivingStep == null)
                {
                    return Result<RatchetState, EcliptixProtocolFailure>.Ok(proto);
                }

                Result<ChainStepState, EcliptixProtocolFailure> receivingStepStateResult =
                    _receivingStep.ToProtoState();
                if (receivingStepStateResult.IsErr)
                {
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(receivingStepStateResult.UnwrapErr());
                }

                proto.ReceivingStep = receivingStepStateResult.Unwrap();

                return Result<RatchetState, EcliptixProtocolFailure>.Ok(proto);
            }
            catch (Exception ex)
            {
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.FAILED_TO_EXPORT_PROTO_STATE, ex));
            }
        }
    }

    public static Result<EcliptixProtocolConnection, EcliptixProtocolFailure> FromProtoState(uint connectId,
        RatchetState proto, RatchetConfig ratchetConfig, PubKeyExchangeType exchangeType)
    {
        EcliptixProtocolChainStep? sendingStep = null;
        EcliptixProtocolChainStep? receivingStep = null;
        SodiumSecureMemoryHandle? rootKeyHandle = null;

        try
        {
            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult =
                EcliptixProtocolChainStep.FromProtoState(ChainStepType.Sender, proto.SendingStep);
            if (sendingStepResult.IsErr)
            {
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());
            }

            sendingStep = sendingStepResult.Unwrap();

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EcliptixProtocolChainStep.FromProtoState(ChainStepType.Receiver, proto.ReceivingStep);
            if (receivingStepResult.IsErr)
            {
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());
            }

            receivingStep = receivingStepResult.Unwrap();

            Result<SodiumSecureMemoryHandle, SodiumFailure> rootKeyAllocResult =
                SodiumSecureMemoryHandle.Allocate(proto.RootKey.Length);
            if (rootKeyAllocResult.IsErr)
            {
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    rootKeyAllocResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            rootKeyHandle = rootKeyAllocResult.Unwrap();

            Result<Unit, SodiumFailure> copyResult =
                SecureByteStringInterop.CopyFromByteStringToSecureMemory(proto.RootKey, rootKeyHandle);
            if (copyResult.IsErr)
            {
                rootKeyHandle.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    copyResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            EcliptixProtocolConnection connection = new(connectId, proto, sendingStep, receivingStep, rootKeyHandle,
                ratchetConfig, exchangeType);

            sendingStep = null;
            receivingStep = null;
            rootKeyHandle = null;

            Result<Unit, EcliptixProtocolFailure> metadataKeyResult = connection.DeriveMetadataEncryptionKey();
            return metadataKeyResult.IsErr
                ? Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(metadataKeyResult.UnwrapErr())
                : Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Ok(connection);
        }
        catch (Exception ex)
        {
            sendingStep?.Dispose();
            receivingStep?.Dispose();
            rootKeyHandle?.Dispose();
            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.FAILED_TO_REHYDRATE_FROM_PROTO_STATE,
                    ex));
        }
    }

    public Result<LocalPublicKeyBundle, EcliptixProtocolFailure> GetPeerBundle()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
            }

            if (_peerBundle != null)
            {
                return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Ok(_peerBundle);
            }

            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.PEER_BUNDLE_NOT_SET));
        }
    }

    public bool IsInitiator()
    {
        return _isInitiator;
    }

    internal Result<Unit, EcliptixProtocolFailure> SetPeerBundle(LocalPublicKeyBundle peerBundle)
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return disposedCheck;
            }

            _peerBundle = peerBundle;
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> FinalizeChainAndDhKeys(byte[] initialRootKey,
        byte[] initialPeerDhPublicKey)
    {
        lock (_lock)
        {
            byte[]? persistentPrivKeyBytes = null;
            byte[]? peerDhPublicCopy = null;
            byte[]? senderChainKey = null;
            byte[]? receiverChainKey = null;

            try
            {
                Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
                if (disposedCheck.IsErr)
                {
                    return disposedCheck;
                }

                Result<Unit, EcliptixProtocolFailure> finalizedCheck = CheckIfNotFinalized();
                if (finalizedCheck.IsErr)
                {
                    return finalizedCheck;
                }

                Result<Unit, EcliptixProtocolFailure> keyValidation =
                    ValidateInitialKeys(initialRootKey, initialPeerDhPublicKey);
                if (keyValidation.IsErr)
                {
                    return keyValidation;
                }

                peerDhPublicCopy = (byte[])initialPeerDhPublicKey.Clone();
                Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                    SodiumSecureMemoryHandle.Allocate(Constants.X_25519_KEY_SIZE);
                if (allocResult.IsErr)
                {
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        allocResult.UnwrapErr().ToEcliptixProtocolFailure());
                }

                Result<byte[], SodiumFailure> initialKeyReadResult =
                    _initialSendingDhPrivateKeyHandle!.ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE);
                if (initialKeyReadResult.IsErr)
                {
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        initialKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
                }

                persistentPrivKeyBytes = initialKeyReadResult.Unwrap();
                byte[]? dhSecret = null;
                byte[]? newRootKey = null;

                try
                {
                    dhSecret = ScalarMult.Mult(persistentPrivKeyBytes, peerDhPublicCopy);
                }
                catch (Exception ex)
                {
                    WipeIfNotNull(peerDhPublicCopy);
                    WipeIfNotNull(dhSecret);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.DeriveKey(
                            "Failed to compute DH shared secret during initial handshake.", ex));
                }

                SodiumSecureMemoryHandle tempRootHandle = allocResult.Unwrap();

                try
                {
                    using SecurePooledArray<byte> hkdfOutputBuffer =
                        SecureArrayPool.Rent<byte>(Constants.X_25519_KEY_SIZE *
                                                   ProtocolSystemConstants.Protocol.HKDF_OUTPUT_BUFFER_MULTIPLIER);
                    Span<byte> hkdfOutputSpan = hkdfOutputBuffer.AsSpan();
                    HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        ikm: dhSecret!,
                        output: hkdfOutputSpan,
                        salt: initialRootKey,
                        info: DhRatchetInfo
                    );

                    newRootKey = hkdfOutputSpan[..Constants.X_25519_KEY_SIZE].ToArray();
                    Result<Unit, SodiumFailure> writeResult = tempRootHandle.Write(newRootKey);
                    if (writeResult.IsErr)
                    {
                        tempRootHandle.Dispose();
                        WipeIfNotNull(peerDhPublicCopy);
                        WipeIfNotNull(dhSecret);
                        WipeIfNotNull(newRootKey);
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            writeResult.UnwrapErr().ToEcliptixProtocolFailure());
                    }

                    Span<byte> sendSpan = stackalloc byte[Constants.X_25519_KEY_SIZE];
                    Span<byte> recvSpan = stackalloc byte[Constants.X_25519_KEY_SIZE];

                    HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        ikm: newRootKey,
                        output: sendSpan,
                        salt: null,
                        info: InitialSenderChainInfo
                    );

                    HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        ikm: newRootKey,
                        output: recvSpan,
                        salt: null,
                        info: InitialReceiverChainInfo
                    );

                    if (_isInitiator)
                    {
                        senderChainKey = sendSpan.ToArray();
                        receiverChainKey = recvSpan.ToArray();
                    }
                    else
                    {
                        senderChainKey = recvSpan.ToArray();
                        receiverChainKey = sendSpan.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    tempRootHandle.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    WipeIfNotNull(dhSecret);
                    WipeIfNotNull(newRootKey);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.DeriveKey(
                            EcliptixProtocolFailureMessages.FAILED_TO_DERIVE_INITIAL_CHAIN_KEYS, ex));
                }
                finally
                {
                    WipeIfNotNull(dhSecret);
                    WipeIfNotNull(newRootKey);
                }

                Result<Unit, EcliptixProtocolFailure> updateResult =
                    _sendingStep.UpdateKeysAfterDhRatchet(senderChainKey);
                if (updateResult.IsErr)
                {
                    tempRootHandle.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return updateResult;
                }

                Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> createResult =
                    EcliptixProtocolChainStep.Create(ChainStepType.Receiver,
                        receiverChainKey, persistentPrivKeyBytes, _persistentDhPublicKey);
                if (createResult.IsErr)
                {
                    tempRootHandle.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(createResult.UnwrapErr());
                }

                _rootKeyHandle = tempRootHandle;
                _receivingStep = createResult.Unwrap();
                _peerDhPublicKey = peerDhPublicCopy;
                peerDhPublicCopy = null;

                Result<Unit, EcliptixProtocolFailure> metadataKeyResult = DeriveMetadataEncryptionKey();
                if (metadataKeyResult.IsErr)
                {
                    return metadataKeyResult;
                }

                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }
            finally
            {
                WipeIfNotNull(persistentPrivKeyBytes);
                WipeIfNotNull(peerDhPublicCopy);
                WipeIfNotNull(senderChainKey);
                WipeIfNotNull(receiverChainKey);
            }
        }
    }

    internal Result<(RatchetChainKey RatchetKey, bool IncludeDhKey), EcliptixProtocolFailure> PrepareNextSendMessage()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> expiredCheck = EnsureNotExpired();
            if (expiredCheck.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(expiredCheck.UnwrapErr());
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult =
                EnsureSendingStepInitialized();
            if (sendingStepResult.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());
            }

            EcliptixProtocolChainStep sendingStep = sendingStepResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> ratchetResult = MaybePerformSendingDhRatchet(sendingStep);
            if (ratchetResult.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());
            }

            bool includeDhKey = ratchetResult.Unwrap();

            Result<uint, EcliptixProtocolFailure> currentIndexResult = sendingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());
            }

            uint currentIndex = currentIndexResult.Unwrap();

            Result<RatchetChainKey, EcliptixProtocolFailure> derivedKeyResult =
                sendingStep.GetOrDeriveKeyFor(currentIndex + 1);
            if (derivedKeyResult.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(derivedKeyResult.UnwrapErr());
            }

            RatchetChainKey derivedKey = derivedKeyResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> setIndexResult = sendingStep.SetCurrentIndex(currentIndex + 1);
            if (setIndexResult.IsErr)
            {
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());
            }

            _sendingStep.PruneOldKeys();
            return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Ok(
                (derivedKey, includeDhKey));
        }
    }

    internal Result<RatchetChainKey, EcliptixProtocolFailure> ProcessReceivedMessage(uint receivedIndex)
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> expiredCheck = EnsureNotExpired();
            if (expiredCheck.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(expiredCheck.UnwrapErr());
            }

            if (receivedIndex > uint.MaxValue - ProtocolSystemConstants.RatchetRecovery.INDEX_OVERFLOW_BUFFER)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(string.Format(
                        EcliptixProtocolFailureMessages.RECEIVED_INDEX_TOO_LARGE,
                        receivedIndex)));
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());
            }

            EcliptixProtocolChainStep receivingStep = receivingStepResult.Unwrap();

            Result<Option<RatchetChainKey>, EcliptixProtocolFailure> recoveryResult =
                _ratchetRecovery.TryRecoverMessageKey(receivedIndex);
            if (recoveryResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(recoveryResult.UnwrapErr());
            }

            if (recoveryResult.Unwrap().IsSome)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Ok(recoveryResult.Unwrap().Value!);
            }

            Result<uint, EcliptixProtocolFailure> currentIndexResult = receivingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());
            }

            uint currentIndex = currentIndexResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> skipResult =
                HandleSkippedKeys(receivingStep, currentIndex, receivedIndex);
            if (skipResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(skipResult.UnwrapErr());
            }

            Result<RatchetChainKey, EcliptixProtocolFailure> derivedKeyResult =
                receivingStep.GetOrDeriveKeyFor(receivedIndex);
            if (derivedKeyResult.IsErr)
            {
                return derivedKeyResult;
            }

            RatchetChainKey derivedKey = derivedKeyResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> setIndexResult = receivingStep.SetCurrentIndex(derivedKey.Index);
            if (setIndexResult.IsErr)
            {
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());
            }

            PerformCleanupIfNeeded(receivedIndex);

            return Result<RatchetChainKey, EcliptixProtocolFailure>.Ok(derivedKey);
        }
    }

    private Result<bool, EcliptixProtocolFailure> HandleSkippedKeys(
        EcliptixProtocolChainStep receivingStep,
        uint currentIndex,
        uint receivedIndex)
    {
        if (receivedIndex <= currentIndex + 1)
        {
            return Result<bool, EcliptixProtocolFailure>.Ok(false);
        }

        Result<byte[], EcliptixProtocolFailure> chainKeyResult = receivingStep.GetCurrentChainKey();
        if (chainKeyResult.IsErr)
        {
            return Result<bool, EcliptixProtocolFailure>.Ok(false);
        }

        byte[] currentChainKey = chainKeyResult.Unwrap();
        try
        {
            Result<Unit, EcliptixProtocolFailure> storeResult = _ratchetRecovery.StoreSkippedMessageKeys(
                currentChainKey,
                currentIndex + 1,
                receivedIndex
            );

            return storeResult.IsErr
                ? Result<bool, EcliptixProtocolFailure>.Err(storeResult.UnwrapErr())
                : Result<bool, EcliptixProtocolFailure>.Ok(true);
        }
        finally
        {
            SodiumInterop.SecureWipe(currentChainKey);
        }
    }

    private void PerformCleanupIfNeeded(uint receivedIndex)
    {
        if (receivedIndex > ProtocolSystemConstants.RatchetRecovery.CLEANUP_THRESHOLD)
        {
            _ratchetRecovery.CleanupOldKeys(receivedIndex - ProtocolSystemConstants.RatchetRecovery.CLEANUP_THRESHOLD);
        }

        _receivingStep!.PruneOldKeys();
    }

    public Result<Unit, EcliptixProtocolFailure> PerformReceivingRatchet(byte[] receivedDhKey)
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return disposedCheck;
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());
            }

            EcliptixProtocolChainStep receivingStep = receivingStepResult.Unwrap();

            Result<uint, EcliptixProtocolFailure> currentIndexResult = receivingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());
            }

            uint currentIndex = currentIndexResult.Unwrap();
            DateTime lastRatchetTime = new DateTime(Interlocked.Read(ref _lastRatchetTimeTicks), DateTimeKind.Utc);
            bool shouldRatchetNow = _isFirstReceivingRatchet ||
                                    _ratchetConfig.ShouldRatchet(currentIndex + 1, lastRatchetTime, _receivedNewDhKey);

            if (!shouldRatchetNow)
            {
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }

            _isFirstReceivingRatchet = false;
            return PerformDhRatchet(isSender: false, receivedDhPublicKeyBytes: receivedDhKey);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformDhRatchet(bool isSender,
        byte[]? receivedDhPublicKeyBytes = null)
    {
        DhRatchetContext context = new();

        try
        {
            Result<Unit, EcliptixProtocolFailure> validationResult =
                ValidateDhRatchetPreConditions(receivedDhPublicKeyBytes);
            if (validationResult.IsErr)
            {
                return validationResult;
            }

            Result<byte[], EcliptixProtocolFailure> dhSecretResult =
                ComputeDhSecret(isSender, receivedDhPublicKeyBytes, context);
            if (dhSecretResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(dhSecretResult.UnwrapErr());
            }

            context.DhSecret = dhSecretResult.Unwrap();

            Result<(byte[], byte[]), EcliptixProtocolFailure> keysResult =
                DeriveRatchetKeys(context.DhSecret);
            if (keysResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(keysResult.UnwrapErr());
            }

            (context.NewRootKey, context.NewChainKey) = keysResult.Unwrap();

            Result<Unit, SodiumFailure> writeResult = _rootKeyHandle!.Write(context.NewRootKey);
            if (writeResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    writeResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            Result<Unit, EcliptixProtocolFailure> updateResult =
                UpdateChainStepAfterRatchet(isSender, context, receivedDhPublicKeyBytes);
            if (updateResult.IsErr)
            {
                return updateResult;
            }

            FinalizeRatchet();

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            context.Dispose();
        }
    }

    private Result<Unit, EcliptixProtocolFailure> ValidateDhRatchetPreConditions(byte[]? receivedDhPublicKeyBytes)
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return disposedCheck;
        }

        if (receivedDhPublicKeyBytes != null)
        {
            Result<Unit, EcliptixProtocolFailure> dhValidationResult =
                DhValidator.ValidateX25519PublicKey(receivedDhPublicKeyBytes);
            if (dhValidationResult.IsErr)
            {
                return dhValidationResult;
            }
        }

        return _rootKeyHandle is not { IsInvalid: false }
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.ROOT_KEY_HANDLE_NOT_INITIALIZED))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<byte[], EcliptixProtocolFailure> ComputeDhSecret(
        bool isSender,
        byte[]? receivedDhPublicKeyBytes,
        DhRatchetContext context)
    {
        try
        {
            return isSender
                ? ComputeSenderDhSecret(context)
                : ComputeReceiverDhSecret(receivedDhPublicKeyBytes, context);
        }
        catch (Exception ex)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages.DH_CALCULATION_FAILED_DURING_RATCHET,
                    ex));
        }
    }

    private Result<byte[], EcliptixProtocolFailure> ComputeSenderDhSecret(DhRatchetContext context)
    {
        if (_peerDhPublicKey == null)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages
                    .SENDER_RATCHET_PRE_CONDITIONS_NOT_MET));
        }

        Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> ephResult =
            SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.EPHEMERAL_DH_RATCHET_KEY_PURPOSE);
        if (ephResult.IsErr)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(ephResult.UnwrapErr());
        }

        (context.NewEphemeralSkHandle, context.NewEphemeralPublicKey) = ephResult.Unwrap();

        Result<byte[], SodiumFailure> privateKeyReadResult =
            context.NewEphemeralSkHandle.ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE);
        if (privateKeyReadResult.IsErr)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                privateKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        context.LocalPrivateKeyBytes = privateKeyReadResult.Unwrap();

        byte[] dhSecret = ScalarMult.Mult(context.LocalPrivateKeyBytes, _peerDhPublicKey);
        return Result<byte[], EcliptixProtocolFailure>.Ok(dhSecret);
    }

    private Result<byte[], EcliptixProtocolFailure> ComputeReceiverDhSecret(
        byte[]? receivedDhPublicKeyBytes,
        DhRatchetContext context)
    {
        if (_receivingStep == null || receivedDhPublicKeyBytes is not { Length: Constants.X_25519_PUBLIC_KEY_SIZE })
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages
                    .RECEIVER_RATCHET_PRE_CONDITIONS_NOT_MET));
        }

        Result<byte[], SodiumFailure> privateKeyReadResult =
            _currentSendingDhPrivateKeyHandle!.ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE);
        if (privateKeyReadResult.IsErr)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                privateKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        context.LocalPrivateKeyBytes = privateKeyReadResult.Unwrap();

        byte[] dhSecret = ScalarMult.Mult(context.LocalPrivateKeyBytes, receivedDhPublicKeyBytes);
        return Result<byte[], EcliptixProtocolFailure>.Ok(dhSecret);
    }

    private Result<(byte[], byte[]), EcliptixProtocolFailure> DeriveRatchetKeys(byte[] dhSecret)
    {
        Result<byte[], SodiumFailure> rootKeyReadResult = _rootKeyHandle!.ReadBytes(Constants.X_25519_KEY_SIZE);
        if (rootKeyReadResult.IsErr)
        {
            return Result<(byte[], byte[]), EcliptixProtocolFailure>.Err(
                rootKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        byte[] currentRootKey = rootKeyReadResult.Unwrap();

        try
        {
            using SecurePooledArray<byte> hkdfOutputBuffer =
                SecureArrayPool.Rent<byte>(Constants.X_25519_KEY_SIZE *
                                           ProtocolSystemConstants.Protocol.HKDF_OUTPUT_BUFFER_MULTIPLIER);
            Span<byte> hkdfOutputSpan = hkdfOutputBuffer.AsSpan();

            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: dhSecret,
                output: hkdfOutputSpan,
                salt: currentRootKey,
                info: DhRatchetInfo
            );

            byte[] newRootKey = hkdfOutputSpan[..Constants.X_25519_KEY_SIZE].ToArray();
            byte[] newChainKey = hkdfOutputSpan[Constants.X_25519_KEY_SIZE..].ToArray();

            return Result<(byte[], byte[]), EcliptixProtocolFailure>.Ok((newRootKey, newChainKey));
        }
        finally
        {
            SodiumInterop.SecureWipe(currentRootKey);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> UpdateChainStepAfterRatchet(
        bool isSender,
        DhRatchetContext context,
        byte[]? receivedDhPublicKeyBytes)
    {
        return isSender
            ? UpdateSendingChainStep(context)
            : UpdateReceivingChainStep(context, receivedDhPublicKeyBytes);
    }

    private Result<Unit, EcliptixProtocolFailure> UpdateSendingChainStep(DhRatchetContext context)
    {
        Result<byte[], SodiumFailure> newDhPrivateKeyResult =
            context.NewEphemeralSkHandle!.ReadBytes(Constants.X_25519_PRIVATE_KEY_SIZE);
        if (newDhPrivateKeyResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                newDhPrivateKeyResult.UnwrapErr().ToEcliptixProtocolFailure());
        }

        context.NewDhPrivateKeyBytes = newDhPrivateKeyResult.Unwrap();

        _currentSendingDhPrivateKeyHandle?.Dispose();
        _currentSendingDhPrivateKeyHandle = context.NewEphemeralSkHandle;
        context.NewEphemeralSkHandle = null;

        return _sendingStep.UpdateKeysAfterDhRatchet(
            context.NewChainKey!,
            context.NewDhPrivateKeyBytes,
            context.NewEphemeralPublicKey);
    }

    private Result<Unit, EcliptixProtocolFailure> UpdateReceivingChainStep(
        DhRatchetContext context,
        byte[]? receivedDhPublicKeyBytes)
    {
        Result<Unit, EcliptixProtocolFailure> updateResult =
            _receivingStep!.UpdateKeysAfterDhRatchet(context.NewChainKey!);

        if (updateResult.IsOk)
        {
            WipeIfNotNull(_peerDhPublicKey);
            _peerDhPublicKey = (byte[])receivedDhPublicKeyBytes!.Clone();
        }

        return updateResult;
    }

    private void FinalizeRatchet()
    {
        _replayProtection.OnRatchetRotation();
        _receivedNewDhKey = false;
        Interlocked.Exchange(ref _lastRatchetTimeTicks, DateTime.UtcNow.Ticks);

        Result<Unit, EcliptixProtocolFailure> metadataKeyResult = DeriveMetadataEncryptionKey();
        if (metadataKeyResult.IsErr)
        {
            return;
        }

        _eventHandler?.OnProtocolStateChanged(_id);
    }

    private sealed class DhRatchetContext : IDisposable
    {
        public byte[]? DhSecret;
        public byte[]? NewRootKey;
        public byte[]? NewChainKey;
        public byte[]? NewEphemeralPublicKey;
        public byte[]? LocalPrivateKeyBytes;
        public byte[]? NewDhPrivateKeyBytes;
        public SodiumSecureMemoryHandle? NewEphemeralSkHandle;

        public void Dispose()
        {
            WipeIfNotNull(DhSecret);
            WipeIfNotNull(NewRootKey);
            WipeIfNotNull(NewChainKey);
            WipeIfNotNull(NewEphemeralPublicKey);
            WipeIfNotNull(LocalPrivateKeyBytes);
            WipeIfNotNull(NewDhPrivateKeyBytes);
            NewEphemeralSkHandle?.Dispose();
        }
    }

    internal Result<byte[], EcliptixProtocolFailure> GenerateNextNonce()
    {
        if (_disposed)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolConnection)));
        }

        long currentCounter = Volatile.Read(ref _nonceCounter);
        if (currentCounter >= ProtocolSystemConstants.Protocol.MAX_NONCE_COUNTER)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.NONCE_COUNTER_OVERFLOW));
        }

        using SecurePooledArray<byte> pooledNonce = SecureArrayPool.Rent<byte>(Constants.AES_GCM_NONCE_SIZE);
        Span<byte> nonceBuffer = pooledNonce.AsSpan();

        RandomNumberGenerator.Fill(nonceBuffer[..ProtocolSystemConstants.Protocol.RANDOM_NONCE_PREFIX_SIZE]);
        long nextCounter = Interlocked.Increment(ref _nonceCounter);
        if (nextCounter > ProtocolSystemConstants.Protocol.MAX_NONCE_COUNTER)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.NONCE_COUNTER_OVERFLOW));
        }

        uint currentNonce = (uint)nextCounter;
        BinaryPrimitives.WriteUInt32LittleEndian(nonceBuffer[Constants.U_INT_32_LITTLE_ENDIAN_OFFSET..], currentNonce);

        return Result<byte[], EcliptixProtocolFailure>.Ok(nonceBuffer.ToArray());
    }

    public Result<byte[]?, EcliptixProtocolFailure> GetCurrentPeerDhPublicKey()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return Result<byte[]?, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
            }

            byte[]? result = _peerDhPublicKey != null ? (byte[])_peerDhPublicKey.Clone() : null;
            return Result<byte[]?, EcliptixProtocolFailure>.Ok(result);
        }
    }

    public Result<byte[]?, EcliptixProtocolFailure> GetCurrentSenderDhPublicKey()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return Result<byte[]?, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> stepResult = EnsureSendingStepInitialized();
            if (stepResult.IsErr)
            {
                return Result<byte[]?, EcliptixProtocolFailure>.Err(stepResult.UnwrapErr());
            }

            Result<byte[]?, EcliptixProtocolFailure> keyResult = stepResult.Unwrap().ReadDhPublicKey();

            return keyResult;
        }
    }

    private Result<Unit, EcliptixProtocolFailure> DeriveMetadataEncryptionKey()
    {
        if (_rootKeyHandle is not { IsInvalid: false })
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Root key handle not initialized for metadata key derivation"));
        }

        byte[]? rootKeyBytes = null;
        byte[]? metadataKeyBytes = null;
        try
        {
            Result<byte[], SodiumFailure> readResult =
                _rootKeyHandle.ReadBytes(Constants.X_25519_KEY_SIZE);
            if (readResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(readResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            rootKeyBytes = readResult.Unwrap();

            metadataKeyBytes = new byte[Constants.AES_KEY_SIZE];
            ReadOnlySpan<byte> info = Encoding.UTF8.GetBytes("ecliptix-metadata-v1");

            HKDF.DeriveKey(
                HashAlgorithmName.SHA256,
                ikm: rootKeyBytes,
                output: metadataKeyBytes.AsSpan(),
                salt: null,
                info: info
            );

            _metadataEncryptionKeyHandle?.Dispose();
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(Constants.AES_KEY_SIZE);
            if (allocResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            _metadataEncryptionKeyHandle = allocResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = _metadataEncryptionKeyHandle.Write(metadataKeyBytes);
            if (writeResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey("Failed to derive metadata encryption key", ex));
        }
        finally
        {
            if (rootKeyBytes != null)
            {
                SodiumInterop.SecureWipe(rootKeyBytes);
            }

            if (metadataKeyBytes != null)
            {
                SodiumInterop.SecureWipe(metadataKeyBytes);
            }
        }
    }

    public Result<byte[], EcliptixProtocolFailure> GetMetadataEncryptionKey()
    {
        if (_metadataEncryptionKeyHandle is not { IsInvalid: false })
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Metadata encryption key not initialized"));
        }

        Result<byte[], SodiumFailure> readResult = _metadataEncryptionKeyHandle.ReadBytes(Constants.AES_KEY_SIZE);
        return readResult.IsOk
            ? Result<byte[], EcliptixProtocolFailure>.Ok(readResult.Unwrap())
            : Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr().ToEcliptixProtocolFailure());
    }

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (disposing)
            {
                SecureCleanupLogic();
            }
        }
    }

    public Result<Unit, EcliptixProtocolFailure> SyncWithRemoteState(uint remoteSendingChainLength,
        uint remoteReceivingChainLength)
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
            {
                return disposedCheck;
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> receivingSkipResult =
                receivingStepResult.Unwrap().SkipKeysUntil(remoteSendingChainLength);
            if (receivingSkipResult.IsErr)
            {
                return receivingSkipResult;
            }

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult =
                EnsureSendingStepInitialized();
            if (sendingStepResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> sendingSkipResult =
                sendingStepResult.Unwrap().SkipKeysUntil(remoteReceivingChainLength);
            if (sendingSkipResult.IsErr)
            {
                return sendingSkipResult;
            }

            _eventHandler?.OnProtocolStateChanged(_id);

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
    }

    public void NotifyRatchetRotation()
    {
        _replayProtection.OnRatchetRotation();
    }

    ~EcliptixProtocolConnection()
    {
        Dispose(false);
    }

    internal Result<Unit, EcliptixProtocolFailure> CheckReplayProtection(byte[] nonce, ulong messageIndex)
    {
        return _replayProtection.CheckAndRecordMessage(nonce, messageIndex,
            chainIndex: ProtocolSystemConstants.Protocol.DEFAULT_CHAIN_INDEX);
    }

    private void SecureCleanupLogic()
    {
        _replayProtection.Dispose();
        _ratchetRecovery.Dispose();
        _rootKeyHandle?.Dispose();
        _metadataEncryptionKeyHandle?.Dispose();
        _sendingStep.Dispose();
        _receivingStep?.Dispose();
        _persistentDhPrivateKeyHandle?.Dispose();
        if (_currentSendingDhPrivateKeyHandle != _initialSendingDhPrivateKeyHandle)
        {
            _currentSendingDhPrivateKeyHandle?.Dispose();
        }

        _initialSendingDhPrivateKeyHandle?.Dispose();
        WipeIfNotNull(_peerDhPublicKey);
        WipeIfNotNull(_persistentDhPublicKey);
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed()
    {
        return _disposed
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(EcliptixProtocolConnection)))
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static void WipeIfNotNull(byte[]? data)
    {
        if (data is not null)
        {
            SodiumInterop.SecureWipe(data);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> EnsureNotExpired()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return disposedCheck;
        }

        if (DateTimeOffset.UtcNow - _createdAt > SessionTimeout)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.SESSION_EXPIRED, _id)));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureSendingStepInitialized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        return disposedCheck.IsErr
            ? Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr())
            : Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_sendingStep);
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureReceivingStepInitialized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());
        }

        if (_receivingStep != null)
        {
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_receivingStep);
        }

        return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.RECEIVING_CHAIN_STEP_NOT_INITIALIZED));
    }

    private Result<Unit, EcliptixProtocolFailure> CheckIfNotFinalized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
        {
            return disposedCheck;
        }

        if (_rootKeyHandle != null || _receivingStep != null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.SESSION_ALREADY_FINALIZED));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateInitialKeys(byte[] rootKey, byte[] peerDhKey)
    {
        if (rootKey.Length != Constants.X_25519_KEY_SIZE)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.INITIAL_ROOT_KEY_INVALID_SIZE,
                        Constants.X_25519_KEY_SIZE)));
        }

        if (peerDhKey.Length != Constants.X_25519_PUBLIC_KEY_SIZE)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.INITIAL_PEER_DH_PUBLIC_KEY_INVALID_SIZE,
                        Constants.X_25519_PUBLIC_KEY_SIZE)));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<bool, EcliptixProtocolFailure> MaybePerformSendingDhRatchet(EcliptixProtocolChainStep sendingStep)
    {
        Result<uint, EcliptixProtocolFailure> currentIndexResult = sendingStep.GetCurrentIndex();
        if (currentIndexResult.IsErr)
        {
            return Result<bool, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());
        }

        uint currentIndex = currentIndexResult.Unwrap();
        DateTime lastRatchetTime = new DateTime(Interlocked.Read(ref _lastRatchetTimeTicks), DateTimeKind.Utc);
        bool shouldRatchet = _ratchetConfig.ShouldRatchet(currentIndex + 1, lastRatchetTime, _receivedNewDhKey);


        if (!shouldRatchet)
        {
            return Result<bool, EcliptixProtocolFailure>.Ok(false);
        }


        Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformDhRatchet(true);
        if (ratchetResult.IsErr)
        {
            return Result<bool, EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());
        }


        _receivedNewDhKey = false;
        return Result<bool, EcliptixProtocolFailure>.Ok(true);
    }
}
