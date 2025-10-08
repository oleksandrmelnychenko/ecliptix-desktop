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

public sealed class EcliptixProtocolConnection : IDisposable
{
    private static readonly TimeSpan SessionTimeout = ProtocolSystemConstants.Timeouts.SessionTimeout;
    private static ReadOnlySpan<byte> InitialSenderChainInfo => Encoding.UTF8.GetBytes(ProtocolSystemConstants.Protocol.InitialSenderChainInfo);
    private static ReadOnlySpan<byte> InitialReceiverChainInfo => Encoding.UTF8.GetBytes(ProtocolSystemConstants.Protocol.InitialReceiverChainInfo);
    private static ReadOnlySpan<byte> DhRatchetInfo => Encoding.UTF8.GetBytes(ProtocolSystemConstants.Protocol.DhRatchetInfo);

    private readonly Lock _lock = new();
    private readonly ReplayProtection _replayProtection = new();
    private readonly RatchetConfig _ratchetConfig;
    private readonly RatchetRecovery _ratchetRecovery = new();
    private readonly PerformanceProfiler _profiler = new();

    private readonly DateTimeOffset _createdAt;
    private readonly uint _id;
    private DateTime _lastRatchetTime = DateTime.UtcNow;
    private bool _isFirstReceivingRatchet;
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
    private bool _receivedNewDhKey;
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
        _nonceCounter = ProtocolSystemConstants.Protocol.InitialNonceCounter;
        _createdAt = DateTimeOffset.UtcNow;
        _peerDhPublicKey = null;
        _receivedNewDhKey = false;
        _disposed = false;

    }

    private EcliptixProtocolConnection(uint id, RatchetState proto, EcliptixProtocolChainStep sendingStep,
        EcliptixProtocolChainStep? receivingStep, SodiumSecureMemoryHandle rootKeyHandle, RatchetConfig ratchetConfig, PubKeyExchangeType exchangeType)
    {
        _id = id;
        _isInitiator = proto.IsInitiator;
        _createdAt = proto.CreatedAt.ToDateTimeOffset();
        _nonceCounter = (long)proto.NonceCounter;
        _peerBundle = LocalPublicKeyBundle.FromProtobufExchange(proto.PeerBundle).Unwrap();
        if (!proto.PeerDhPublicKey.IsEmpty)
        {
            SecureByteStringInterop.SecureCopyWithCleanup(proto.PeerDhPublicKey, out byte[]? peerDhKey);
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

    public void SetEventHandler(IProtocolEventHandler? handler)
    {
        _eventHandler = handler;
    }

    public void Dispose()
    {
        Dispose(true);
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
                SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.InitialSendingDhKeyPurpose);
            if (initialKeysResult.IsErr)
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(initialKeysResult.UnwrapErr());

            (initialSendingDhPrivateKeyHandle, byte[] initialSendingDhPublicKey) = initialKeysResult.Unwrap();

            Result<byte[], SodiumFailure> readBytesResult =
                initialSendingDhPrivateKeyHandle.ReadBytes(Constants.X25519PrivateKeySize);
            if (readBytesResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle?.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    readBytesResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            initialSendingDhPrivateKeyBytes = readBytesResult.Unwrap();

            Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> persistentKeysResult =
                SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.PersistentDhKeyPurpose);
            if (persistentKeysResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle?.Dispose();
                WipeIfNotNull(initialSendingDhPrivateKeyBytes);
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(persistentKeysResult
                    .UnwrapErr());
            }

            (persistentDhPrivateKeyHandle, byte[] persistentDhPublicKey) = persistentKeysResult.Unwrap();

            byte[] tempChainKey = new byte[Constants.X25519KeySize];
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
                initialSendingDhPrivateKeyHandle!, sendingStep, persistentDhPrivateKeyHandle!,
                persistentDhPublicKey!, ratchetConfig, exchangeType);
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
                EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.UnexpectedErrorCreatingSession, connectId), ex));
        }
    }

    public Result<RatchetState, EcliptixProtocolFailure> ToProtoState()
    {
        lock (_lock)
        {
            if (_disposed)
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolConnection)));

            if (_exchangeType == PubKeyExchangeType.ServerStreaming)
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.ServerStreamingNotPersisted));

            try
            {
                Result<ChainStepState, EcliptixProtocolFailure> sendingStepStateResult = _sendingStep.ToProtoState();
                if (sendingStepStateResult.IsErr)
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(sendingStepStateResult.UnwrapErr());

                Result<byte[], SodiumFailure> rootKeyReadResult =
                    _rootKeyHandle!.ReadBytes(Constants.X25519KeySize);
                if (rootKeyReadResult.IsErr)
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(
                        rootKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());

                RatchetState proto = new()
                {
                    IsInitiator = _isInitiator,
                    CreatedAt = Timestamp.FromDateTimeOffset(_createdAt),
                    NonceCounter = (ulong)_nonceCounter,
                    PeerBundle = _peerBundle!.ToProtobufExchange(),
                    PeerDhPublicKey = ByteString.CopyFrom(_peerDhPublicKey ?? []),
                    IsFirstReceivingRatchet = _isFirstReceivingRatchet,
                    RootKey = ByteString.CopyFrom(rootKeyReadResult.Unwrap()),
                    SendingStep = sendingStepStateResult.Unwrap()
                };

                if (_receivingStep == null) return Result<RatchetState, EcliptixProtocolFailure>.Ok(proto);
                Result<ChainStepState, EcliptixProtocolFailure> receivingStepStateResult =
                    _receivingStep.ToProtoState();
                if (receivingStepStateResult.IsErr)
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(receivingStepStateResult.UnwrapErr());
                proto.ReceivingStep = receivingStepStateResult.Unwrap();

                return Result<RatchetState, EcliptixProtocolFailure>.Ok(proto);
            }
            catch (Exception ex)
            {
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.FailedToExportProtoState, ex));
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
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());
            sendingStep = sendingStepResult.Unwrap();

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EcliptixProtocolChainStep.FromProtoState(ChainStepType.Receiver, proto.ReceivingStep);
            if (receivingStepResult.IsErr)
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());
            receivingStep = receivingStepResult.Unwrap();

            Result<SodiumSecureMemoryHandle, SodiumFailure> rootKeyAllocResult =
                SodiumSecureMemoryHandle.Allocate(proto.RootKey.Length);
            if (rootKeyAllocResult.IsErr)
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    rootKeyAllocResult.UnwrapErr().ToEcliptixProtocolFailure());
            rootKeyHandle = rootKeyAllocResult.Unwrap();

            Result<Unit, SodiumFailure> copyResult =
                SecureByteStringInterop.CopyFromByteStringToSecureMemory(proto.RootKey, rootKeyHandle);
            if (copyResult.IsErr)
            {
                rootKeyHandle.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    copyResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            EcliptixProtocolConnection connection = new(connectId, proto, sendingStep, receivingStep, rootKeyHandle, ratchetConfig, exchangeType);

            sendingStep = null;
            receivingStep = null;
            rootKeyHandle = null;

            Result<Unit, EcliptixProtocolFailure> metadataKeyResult = connection.DeriveMetadataEncryptionKey();
            if (metadataKeyResult.IsErr)
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(metadataKeyResult.UnwrapErr());

            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Ok(connection);
        }
        catch (Exception ex)
        {
            sendingStep?.Dispose();
            receivingStep?.Dispose();
            rootKeyHandle?.Dispose();
            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.FailedToRehydrateFromProtoState, ex));
        }
    }

    public Result<LocalPublicKeyBundle, EcliptixProtocolFailure> GetPeerBundle()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            if (_peerBundle != null)
                return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Ok(_peerBundle);

            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.PeerBundleNotSet));
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
                return disposedCheck;

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
                    return disposedCheck;

                Result<Unit, EcliptixProtocolFailure> finalizedCheck = CheckIfNotFinalized();
                if (finalizedCheck.IsErr)
                    return finalizedCheck;

                Result<Unit, EcliptixProtocolFailure> keyValidation =
                    ValidateInitialKeys(initialRootKey, initialPeerDhPublicKey);
                if (keyValidation.IsErr)
                    return keyValidation;

                peerDhPublicCopy = (byte[])initialPeerDhPublicKey.Clone();
                Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                    SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize);
                if (allocResult.IsErr)
                {
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        allocResult.UnwrapErr().ToEcliptixProtocolFailure());
                }

                Result<byte[], SodiumFailure> initialKeyReadResult =
                    _initialSendingDhPrivateKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize);
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
                        EcliptixProtocolFailure.DeriveKey("Failed to compute DH shared secret during initial handshake.", ex));
                }

                SodiumSecureMemoryHandle? tempRootHandle = allocResult.Unwrap();

                try
                {
                    using SecurePooledArray<byte> hkdfOutputBuffer = SecureArrayPool.Rent<byte>(Constants.X25519KeySize * ProtocolSystemConstants.Protocol.HkdfOutputBufferMultiplier);
                    Span<byte> hkdfOutputSpan = hkdfOutputBuffer.AsSpan();

                    HKDF.DeriveKey(
                        HashAlgorithmName.SHA256,
                        ikm: dhSecret!,
                        output: hkdfOutputSpan,
                        salt: initialRootKey,
                        info: DhRatchetInfo
                    );

                    newRootKey = hkdfOutputSpan[..Constants.X25519KeySize].ToArray();
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

                    Span<byte> sendSpan = stackalloc byte[Constants.X25519KeySize];
                    Span<byte> recvSpan = stackalloc byte[Constants.X25519KeySize];

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
                        EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages.FailedToDeriveInitialChainKeys, ex));
                }
                finally
                {
                    WipeIfNotNull(dhSecret);
                    WipeIfNotNull(newRootKey);
                }

                Result<Unit, EcliptixProtocolFailure> updateResult =
                    _sendingStep.UpdateKeysAfterDhRatchet(senderChainKey!);
                if (updateResult.IsErr)
                {
                    tempRootHandle.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return updateResult;
                }

                Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> createResult =
                    EcliptixProtocolChainStep.Create(ChainStepType.Receiver,
                        receiverChainKey!, persistentPrivKeyBytes, _persistentDhPublicKey);
                if (createResult.IsErr)
                {
                    tempRootHandle?.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(createResult.UnwrapErr());
                }

                _rootKeyHandle = tempRootHandle;
                _receivingStep = createResult.Unwrap();
                _peerDhPublicKey = peerDhPublicCopy;
                peerDhPublicCopy = null;

                Result<Unit, EcliptixProtocolFailure> metadataKeyResult = DeriveMetadataEncryptionKey();
                if (metadataKeyResult.IsErr) return metadataKeyResult;

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
        using IDisposable timer = _profiler.StartOperation(EcliptixProtocolFailureMessages.OperationNames.PrepareNextSendMessage);
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> expiredCheck = EnsureNotExpired();
            if (expiredCheck.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(expiredCheck.UnwrapErr());

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult =
                EnsureSendingStepInitialized();
            if (sendingStepResult.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());

            EcliptixProtocolChainStep sendingStep = sendingStepResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> ratchetResult = MaybePerformSendingDhRatchet(sendingStep);
            if (ratchetResult.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());

            bool includeDhKey = ratchetResult.Unwrap();

            Result<uint, EcliptixProtocolFailure> currentIndexResult = sendingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

            uint currentIndex = currentIndexResult.Unwrap();

            Result<RatchetChainKey, EcliptixProtocolFailure> derivedKeyResult =
                sendingStep.GetOrDeriveKeyFor(currentIndex + 1);
            if (derivedKeyResult.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(derivedKeyResult.UnwrapErr());

            RatchetChainKey derivedKey = derivedKeyResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> setIndexResult = sendingStep.SetCurrentIndex(currentIndex + 1);
            if (setIndexResult.IsErr)
                return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());

            _sendingStep.PruneOldKeys();
            return Result<(RatchetChainKey, bool), EcliptixProtocolFailure>.Ok(
                (derivedKey, includeDhKey));
        }
    }

    internal Result<RatchetChainKey, EcliptixProtocolFailure> ProcessReceivedMessage(uint receivedIndex)
    {
        using IDisposable timer = _profiler.StartOperation(EcliptixProtocolFailureMessages.OperationNames.ProcessReceivedMessage);
        bool hasSkippedKeys = false;

        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> expiredCheck = EnsureNotExpired();
            if (expiredCheck.IsErr)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(expiredCheck.UnwrapErr());

            if (receivedIndex > uint.MaxValue - ProtocolSystemConstants.RatchetRecovery.IndexOverflowBuffer)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.ReceivedIndexTooLarge, receivedIndex)));

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());

            EcliptixProtocolChainStep receivingStep = receivingStepResult.Unwrap();

            Result<Option<RatchetChainKey>, EcliptixProtocolFailure> recoveryResult = _ratchetRecovery.TryRecoverMessageKey(receivedIndex);
            if (recoveryResult.IsErr)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(recoveryResult.UnwrapErr());

            if (recoveryResult.Unwrap().HasValue)
            {
                hasSkippedKeys = true;
                _eventHandler?.OnMessageProcessed(_id, receivedIndex, hasSkippedKeys);
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Ok(recoveryResult.Unwrap().Value!);
            }

            Result<uint, EcliptixProtocolFailure> currentIndexResult = receivingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

            uint currentIndex = currentIndexResult.Unwrap();

            if (receivedIndex > currentIndex + 1)
            {
                Result<byte[], EcliptixProtocolFailure> chainKeyResult = receivingStep.GetCurrentChainKey();
                if (chainKeyResult.IsOk)
                {
                    byte[] currentChainKey = chainKeyResult.Unwrap();
                    try
                    {
                        Result<Unit, EcliptixProtocolFailure> storeResult = _ratchetRecovery.StoreSkippedMessageKeys(
                            currentChainKey,
                            currentIndex + 1,
                            receivedIndex
                        );
                        hasSkippedKeys = true;

                        if (storeResult.IsErr)
                        {
                            return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(storeResult.UnwrapErr());
                        }
                    }
                    finally
                    {
                        SodiumInterop.SecureWipe(currentChainKey);
                    }
                }
            }

            Result<RatchetChainKey, EcliptixProtocolFailure> derivedKeyResult =
                receivingStep.GetOrDeriveKeyFor(receivedIndex);
            if (derivedKeyResult.IsErr)
                return derivedKeyResult;

            RatchetChainKey derivedKey = derivedKeyResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> setIndexResult = receivingStep.SetCurrentIndex(derivedKey.Index);
            if (setIndexResult.IsErr)
                return Result<RatchetChainKey, EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());

            if (receivedIndex > ProtocolSystemConstants.RatchetRecovery.CleanupThreshold)
            {
                _ratchetRecovery.CleanupOldKeys(receivedIndex - ProtocolSystemConstants.RatchetRecovery.CleanupThreshold);
            }

            _receivingStep!.PruneOldKeys();
            _eventHandler?.OnMessageProcessed(_id, receivedIndex, hasSkippedKeys);

            return Result<RatchetChainKey, EcliptixProtocolFailure>.Ok(derivedKey);
        }
    }

    public Result<Unit, EcliptixProtocolFailure> PerformReceivingRatchet(byte[] receivedDhKey)
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return disposedCheck;

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult = EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());

            EcliptixProtocolChainStep receivingStep = receivingStepResult.Unwrap();

            Result<uint, EcliptixProtocolFailure> currentIndexResult = receivingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

            uint currentIndex = currentIndexResult.Unwrap();
            bool shouldRatchetNow = _isFirstReceivingRatchet ||
                _ratchetConfig.ShouldRatchet(currentIndex + 1, _lastRatchetTime, _receivedNewDhKey);

            if (!shouldRatchetNow) return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            _isFirstReceivingRatchet = false;
            return PerformDhRatchet(isSender: false, receivedDhPublicKeyBytes: receivedDhKey);

        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformDhRatchet(bool isSender,
        byte[]? receivedDhPublicKeyBytes = null)
    {
        using IDisposable timer = _profiler.StartOperation(EcliptixProtocolFailureMessages.OperationNames.DhRatchet);
        byte[]? dhSecret = null, newRootKey = null, newChainKeyForTargetStep = null, newEphemeralPublicKey = null;
        byte[]? localPrivateKeyBytes = null, currentRootKey = null, newDhPrivateKeyBytes = null;
        SodiumSecureMemoryHandle? newEphemeralSkHandle = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return disposedCheck;

            if (receivedDhPublicKeyBytes != null)
            {
                Result<Unit, EcliptixProtocolFailure> dhValidationResult = DhValidator.ValidateX25519PublicKey(receivedDhPublicKeyBytes);
                if (dhValidationResult.IsErr)
                    return dhValidationResult;
            }

            if (_rootKeyHandle is not { IsInvalid: false })
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.RootKeyHandleNotInitialized));

            try
            {
                if (isSender)
                {
                    if (_peerDhPublicKey == null)
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages.SenderRatchetPreConditionsNotMet));

                    Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> ephResult =
                        SodiumInterop.GenerateX25519KeyPair(EcliptixProtocolFailureMessages.EphemeralDhRatchetKeyPurpose);
                    if (ephResult.IsErr)
                        return ephResult.Map(_ => Unit.Value);

                    (newEphemeralSkHandle, newEphemeralPublicKey) = ephResult.Unwrap();

                    Result<byte[], SodiumFailure> privateKeyReadResult =
                        newEphemeralSkHandle.ReadBytes(Constants.X25519PrivateKeySize);
                    if (privateKeyReadResult.IsErr)
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            privateKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
                    localPrivateKeyBytes = privateKeyReadResult.Unwrap();

                    dhSecret = ScalarMult.Mult(localPrivateKeyBytes, _peerDhPublicKey);
                }
                else
                {
                    if (_receivingStep == null || receivedDhPublicKeyBytes is not
                        { Length: Constants.X25519PublicKeySize })
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages.ReceiverRatchetPreConditionsNotMet));

                    Result<byte[], SodiumFailure> privateKeyReadResult =
                        _currentSendingDhPrivateKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize);
                    if (privateKeyReadResult.IsErr)
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            privateKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
                    localPrivateKeyBytes = privateKeyReadResult.Unwrap();

                    dhSecret = ScalarMult.Mult(localPrivateKeyBytes, receivedDhPublicKeyBytes);
                }
            }
            catch (Exception ex)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.DeriveKey(EcliptixProtocolFailureMessages.DhCalculationFailedDuringRatchet, ex));
            }

            Result<byte[], SodiumFailure> rootKeyReadResult = _rootKeyHandle!.ReadBytes(Constants.X25519KeySize);
            if (rootKeyReadResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    rootKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
            currentRootKey = rootKeyReadResult.Unwrap();

            using SecurePooledArray<byte> hkdfOutputBuffer = SecureArrayPool.Rent<byte>(Constants.X25519KeySize * ProtocolSystemConstants.Protocol.HkdfOutputBufferMultiplier);
            Span<byte> hkdfOutputSpan = hkdfOutputBuffer.AsSpan();

            HKDF.DeriveKey(
                global::System.Security.Cryptography.HashAlgorithmName.SHA256,
                ikm: dhSecret!,
                output: hkdfOutputSpan,
                salt: currentRootKey,
                info: DhRatchetInfo
            );

            newRootKey = new byte[Constants.X25519KeySize];
            newChainKeyForTargetStep = new byte[Constants.X25519KeySize];
            hkdfOutputSpan[..Constants.X25519KeySize].CopyTo(newRootKey);
            hkdfOutputSpan[Constants.X25519KeySize..].CopyTo(newChainKeyForTargetStep);

            Result<Unit, SodiumFailure> writeResult = _rootKeyHandle.Write(newRootKey);
            if (writeResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    writeResult.UnwrapErr().ToEcliptixProtocolFailure());

            Result<Unit, EcliptixProtocolFailure> updateResult;
            if (isSender)
            {
                Result<byte[], SodiumFailure> newDhPrivateKeyResult =
                    newEphemeralSkHandle!.ReadBytes(Constants.X25519PrivateKeySize);
                if (newDhPrivateKeyResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        newDhPrivateKeyResult.UnwrapErr().ToEcliptixProtocolFailure());
                newDhPrivateKeyBytes = newDhPrivateKeyResult.Unwrap();

                _currentSendingDhPrivateKeyHandle?.Dispose();
                _currentSendingDhPrivateKeyHandle = newEphemeralSkHandle;
                newEphemeralSkHandle = null;
                updateResult = _sendingStep.UpdateKeysAfterDhRatchet(newChainKeyForTargetStep, newDhPrivateKeyBytes,
                    newEphemeralPublicKey);
            }
            else
            {
                updateResult = _receivingStep!.UpdateKeysAfterDhRatchet(newChainKeyForTargetStep);
                if (updateResult.IsOk)
                {
                    WipeIfNotNull(_peerDhPublicKey);
                    _peerDhPublicKey = (byte[])receivedDhPublicKeyBytes!.Clone();
                }
            }

            if (updateResult.IsErr) return updateResult;

            _replayProtection.OnRatchetRotation();

            _receivedNewDhKey = false;
            _lastRatchetTime = DateTime.UtcNow;

            Result<Unit, EcliptixProtocolFailure> metadataKeyResult = DeriveMetadataEncryptionKey();
            if (metadataKeyResult.IsErr) return metadataKeyResult;

            if (_eventHandler == null) return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            uint newIndex = isSender
                ? _sendingStep.GetCurrentIndex().UnwrapOr(ProtocolSystemConstants.Protocol.DefaultMessageIndex)
                : _receivingStep!.GetCurrentIndex().UnwrapOr(ProtocolSystemConstants.Protocol.DefaultMessageIndex);
            _eventHandler.OnDhRatchetPerformed(_id, isSender, newIndex);

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            WipeIfNotNull(dhSecret);
            WipeIfNotNull(newRootKey);
            WipeIfNotNull(newChainKeyForTargetStep);
            WipeIfNotNull(newEphemeralPublicKey);
            WipeIfNotNull(localPrivateKeyBytes);
            WipeIfNotNull(currentRootKey);
            WipeIfNotNull(newDhPrivateKeyBytes);
            newEphemeralSkHandle?.Dispose();
        }
    }

    internal Result<byte[], EcliptixProtocolFailure> GenerateNextNonce()
    {
        using IDisposable timer = _profiler.StartOperation(EcliptixProtocolFailureMessages.OperationNames.GenerateNonce);

        if (_disposed)
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolConnection)));

        using SecurePooledArray<byte> pooledNonce = SecureArrayPool.Rent<byte>(Constants.AesGcmNonceSize);
        Span<byte> nonceBuffer = pooledNonce.AsSpan();

        RandomNumberGenerator.Fill(nonceBuffer[..ProtocolSystemConstants.Protocol.RandomNoncePrefixSize]);
        long nextCounter = Interlocked.Increment(ref _nonceCounter);
        if (nextCounter > ProtocolSystemConstants.Protocol.MaxNonceCounter)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.NonceCounterOverflow));
        }
        uint currentNonce = (uint)nextCounter;
        BinaryPrimitives.WriteUInt32LittleEndian(nonceBuffer[Constants.UInt32LittleEndianOffset..], currentNonce);

        return Result<byte[], EcliptixProtocolFailure>.Ok(nonceBuffer.ToArray());
    }

    public Result<byte[]?, EcliptixProtocolFailure> GetCurrentPeerDhPublicKey()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<byte[]?, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

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
                return Result<byte[]?, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> stepResult = EnsureSendingStepInitialized();
            if (stepResult.IsErr)
                return Result<byte[]?, EcliptixProtocolFailure>.Err(stepResult.UnwrapErr());

            Result<byte[]?, EcliptixProtocolFailure> keyResult = stepResult.Unwrap().ReadDhPublicKey();

            return keyResult;
        }
    }

    private Result<Unit, EcliptixProtocolFailure> DeriveMetadataEncryptionKey()
    {
        if (_rootKeyHandle is not { IsInvalid: false })
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Root key handle not initialized for metadata key derivation"));

        byte[]? rootKeyBytes = null;
        byte[]? metadataKeyBytes = null;
        try
        {
            Result<byte[], SodiumFailure> readResult =
                _rootKeyHandle.ReadBytes(Constants.X25519KeySize);
            if (readResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(readResult.UnwrapErr().ToEcliptixProtocolFailure());
            rootKeyBytes = readResult.Unwrap();

            metadataKeyBytes = new byte[Constants.AesKeySize];
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
                SodiumSecureMemoryHandle.Allocate(Constants.AesKeySize);
            if (allocResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr().ToEcliptixProtocolFailure());

            _metadataEncryptionKeyHandle = allocResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = _metadataEncryptionKeyHandle.Write(metadataKeyBytes);
            if (writeResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr().ToEcliptixProtocolFailure());

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.DeriveKey("Failed to derive metadata encryption key", ex));
        }
        finally
        {
            if (rootKeyBytes != null) SodiumInterop.SecureWipe(rootKeyBytes);
            if (metadataKeyBytes != null) SodiumInterop.SecureWipe(metadataKeyBytes);
        }
    }

    public Result<byte[], EcliptixProtocolFailure> GetMetadataEncryptionKey()
    {
        if (_metadataEncryptionKeyHandle is not { IsInvalid: false })
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Metadata encryption key not initialized"));

        Result<byte[], SodiumFailure> readResult = _metadataEncryptionKeyHandle.ReadBytes(Constants.AesKeySize);
        return readResult.IsOk
            ? Result<byte[], EcliptixProtocolFailure>.Ok(readResult.Unwrap())
            : Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr().ToEcliptixProtocolFailure());
    }

    private void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_disposed) return;
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
                return disposedCheck;

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult =
                EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> receivingSkipResult =
                receivingStepResult.Unwrap().SkipKeysUntil(remoteSendingChainLength);
            if (receivingSkipResult.IsErr)
                return receivingSkipResult;

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult =
                EnsureSendingStepInitialized();
            if (sendingStepResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> sendingSkipResult =
                sendingStepResult.Unwrap().SkipKeysUntil(remoteReceivingChainLength);
            if (sendingSkipResult.IsErr)
                return sendingSkipResult;

            _eventHandler!.OnChainSynchronized(_id,
                _sendingStep?.GetCurrentIndex().UnwrapOr(ProtocolSystemConstants.Protocol.DefaultMessageIndex) ?? ProtocolSystemConstants.Protocol.DefaultMessageIndex,
                _receivingStep?.GetCurrentIndex().UnwrapOr(ProtocolSystemConstants.Protocol.DefaultMessageIndex) ?? ProtocolSystemConstants.Protocol.DefaultMessageIndex);

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
        return _replayProtection.CheckAndRecordMessage(nonce, messageIndex, chainIndex: ProtocolSystemConstants.Protocol.DefaultChainIndex);
    }

    public Dictionary<string, (long Count, double AvgMs, double MaxMs, double MinMs)> GetPerformanceMetrics()
    {
        return _profiler.GetMetrics();
    }

    internal PerformanceProfiler GetProfiler()
    {
        return _profiler;
    }

    public void ResetPerformanceMetrics()
    {
        _profiler.Reset();
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
            _currentSendingDhPrivateKeyHandle?.Dispose();
        _initialSendingDhPrivateKeyHandle?.Dispose();
        WipeIfNotNull(_peerDhPublicKey);
        WipeIfNotNull(_persistentDhPublicKey);
    }

    private Result<Unit, EcliptixProtocolFailure> CheckDisposed()
    {
        return _disposed
            ? Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolConnection)))
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
            return disposedCheck;

        if (DateTimeOffset.UtcNow - _createdAt > SessionTimeout)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.SessionExpired, _id)));

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureSendingStepInitialized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

        if (_sendingStep != null)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_sendingStep);

        return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.SendingChainStepNotInitialized));
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureReceivingStepInitialized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

        if (_receivingStep != null)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_receivingStep);

        return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.ReceivingChainStepNotInitialized));
    }

    private Result<Unit, EcliptixProtocolFailure> CheckIfNotFinalized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return disposedCheck;

        if (_rootKeyHandle != null || _receivingStep != null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.SessionAlreadyFinalized));

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateInitialKeys(byte[] rootKey, byte[] peerDhKey)
    {
        if (rootKey.Length != Constants.X25519KeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(string.Format(EcliptixProtocolFailureMessages.InitialRootKeyInvalidSize, Constants.X25519KeySize)));
        if (peerDhKey.Length != Constants.X25519PublicKeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    string.Format(EcliptixProtocolFailureMessages.InitialPeerDhPublicKeyInvalidSize, Constants.X25519PublicKeySize)));
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<bool, EcliptixProtocolFailure> MaybePerformSendingDhRatchet(EcliptixProtocolChainStep sendingStep)
    {
        Result<uint, EcliptixProtocolFailure> currentIndexResult = sendingStep.GetCurrentIndex();
        if (currentIndexResult.IsErr)
            return Result<bool, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

        uint currentIndex = currentIndexResult.Unwrap();
        bool shouldRatchet = _ratchetConfig.ShouldRatchet(currentIndex + 1, _lastRatchetTime, _receivedNewDhKey);


        if (!shouldRatchet) return Result<bool, EcliptixProtocolFailure>.Ok(false);


        Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformDhRatchet(true);
        if (ratchetResult.IsErr)
            return Result<bool, EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());


        _receivedNewDhKey = false;
        return Result<bool, EcliptixProtocolFailure>.Ok(true);
    }
}