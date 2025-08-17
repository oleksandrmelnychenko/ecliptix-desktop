using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Sodium;

namespace Ecliptix.Protocol.System.Core;

public interface IProtocolEventHandler
{
    void OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex);
    void OnChainSynchronized(uint connectId, uint localLength, uint remoteLength);
    void OnMessageProcessed(uint connectId, uint messageIndex, bool hasSkippedKeys);
}

public sealed class EcliptixProtocolConnection : IDisposable
{
    private const int DhRotationInterval = 10;
    private const int AesGcmNonceSize = 12;
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromHours(24);
    private static readonly byte[] InitialSenderChainInfo = "ShieldInitSend"u8.ToArray();
    private static readonly byte[] InitialReceiverChainInfo = "ShieldInitRecv"u8.ToArray();
    private static readonly byte[] DhRatchetInfo = "ShieldDhRatchet"u8.ToArray();

    private readonly Lock _lock = new();

    private readonly DateTimeOffset _createdAt;
    private readonly uint _id;
    private readonly bool _isFirstReceivingRatchet;
    private readonly bool _isInitiator;
    private readonly EcliptixProtocolChainStep _sendingStep;
    private SodiumSecureMemoryHandle? _currentSendingDhPrivateKeyHandle;
    private volatile bool _disposed;
    private readonly SodiumSecureMemoryHandle? _initialSendingDhPrivateKeyHandle;
    private ulong _nonceCounter;
    private PublicKeyBundle? _peerBundle;
    private byte[]? _peerDhPublicKey;
    private readonly SodiumSecureMemoryHandle? _persistentDhPrivateKeyHandle;
    private readonly byte[]? _persistentDhPublicKey;
    private bool _receivedNewDhKey;
    private EcliptixProtocolChainStep? _receivingStep;
    private SodiumSecureMemoryHandle? _rootKeyHandle;
    private IProtocolEventHandler? _eventHandler;

    private EcliptixProtocolConnection(uint id, bool isInitiator, SodiumSecureMemoryHandle initialSendingDh,
        EcliptixProtocolChainStep sendingStep, SodiumSecureMemoryHandle persistentDh, byte[] persistentDhPublic)
    {
        _id = id;
        _isInitiator = isInitiator;
        _initialSendingDhPrivateKeyHandle = initialSendingDh;
        _currentSendingDhPrivateKeyHandle = initialSendingDh;
        _sendingStep = sendingStep;
        _persistentDhPrivateKeyHandle = persistentDh;
        _persistentDhPublicKey = persistentDhPublic;
        _peerBundle = null;
        _receivingStep = null;
        _rootKeyHandle = null;
        _nonceCounter = 0;
        _createdAt = DateTimeOffset.UtcNow;
        _peerDhPublicKey = null;
        _receivedNewDhKey = false;
        _disposed = false;
    }

    private EcliptixProtocolConnection(uint id, RatchetState proto, EcliptixProtocolChainStep sendingStep,
        EcliptixProtocolChainStep? receivingStep, SodiumSecureMemoryHandle rootKeyHandle)
    {
        _id = id;
        _isInitiator = proto.IsInitiator;
        _createdAt = proto.CreatedAt.ToDateTimeOffset();
        _nonceCounter = proto.NonceCounter;
        _peerBundle = PublicKeyBundle.FromProtobufExchange(proto.PeerBundle).Unwrap();
        if (!proto.PeerDhPublicKey.IsEmpty)
        {
            UnsafeMemoryHelpers.SecureCopyWithCleanup(proto.PeerDhPublicKey, out byte[]? peerDhKey);
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
        GC.SuppressFinalize(this);
    }

    public static Result<EcliptixProtocolConnection, EcliptixProtocolFailure> Create(uint connectId, bool isInitiator)
    {
        SodiumSecureMemoryHandle? initialSendingDhPrivateKeyHandle = null;
        byte[]? initialSendingDhPublicKey = null;
        byte[]? initialSendingDhPrivateKeyBytes = null;
        EcliptixProtocolChainStep? sendingStep = null;
        SodiumSecureMemoryHandle? persistentDhPrivateKeyHandle = null;
        byte[]? persistentDhPublicKey = null;

        try
        {
            Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> initialKeysResult = 
                SodiumInterop.GenerateX25519KeyPair("Initial Sending DH");
            if (initialKeysResult.IsErr)
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(initialKeysResult.UnwrapErr());

            (initialSendingDhPrivateKeyHandle, initialSendingDhPublicKey) = initialKeysResult.Unwrap();

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
                SodiumInterop.GenerateX25519KeyPair("Persistent DH");
            if (persistentKeysResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle?.Dispose();
                WipeIfNotNull(initialSendingDhPrivateKeyBytes);
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(persistentKeysResult.UnwrapErr());
            }
            (persistentDhPrivateKeyHandle, persistentDhPublicKey) = persistentKeysResult.Unwrap();

            byte[] tempChainKey = new byte[Constants.X25519KeySize];
            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> stepResult =
                EcliptixProtocolChainStep.Create(ChainStepType.Sender, tempChainKey,
                    initialSendingDhPrivateKeyBytes, initialSendingDhPublicKey);
            WipeIfNotNull(tempChainKey);
            WipeIfNotNull(initialSendingDhPrivateKeyBytes);
            initialSendingDhPrivateKeyBytes = null;
            
            if (stepResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle?.Dispose();
                persistentDhPrivateKeyHandle?.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(stepResult.UnwrapErr());
            }

            sendingStep = stepResult.Unwrap();
            EcliptixProtocolConnection connection = new(connectId, isInitiator,
                initialSendingDhPrivateKeyHandle!, sendingStep, persistentDhPrivateKeyHandle!,
                persistentDhPublicKey!);
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
                EcliptixProtocolFailure.Generic($"Unexpected error creating session {connectId}.", ex));
        }
    }

    public Result<RatchetState, EcliptixProtocolFailure> ToProtoState()
    {
        lock (_lock)
        {
            if (_disposed)
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.ObjectDisposed(nameof(EcliptixProtocolConnection)));

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
                    NonceCounter = _nonceCounter,
                    PeerBundle = _peerBundle!.ToProtobufExchange(),
                    PeerDhPublicKey = ByteString.CopyFrom(_peerDhPublicKey ?? []),
                    IsFirstReceivingRatchet = _isFirstReceivingRatchet,
                    RootKey = ByteString.CopyFrom(rootKeyReadResult.Unwrap()),
                    SendingStep = sendingStepStateResult.Unwrap()
                };

                if (_receivingStep != null)
                {
                    Result<ChainStepState, EcliptixProtocolFailure> receivingStepStateResult =
                        _receivingStep.ToProtoState();
                    if (receivingStepStateResult.IsErr)
                        return Result<RatchetState, EcliptixProtocolFailure>.Err(receivingStepStateResult.UnwrapErr());
                    proto.ReceivingStep = receivingStepStateResult.Unwrap();
                }

                return Result<RatchetState, EcliptixProtocolFailure>.Ok(proto);
            }
            catch (Exception ex)
            {
                return Result<RatchetState, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Failed to export connection to proto state.", ex));
            }
        }
    }

    public static Result<EcliptixProtocolConnection, EcliptixProtocolFailure> FromProtoState(uint connectId,
        RatchetState proto)
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
                UnsafeMemoryHelpers.CopyFromByteStringToSecureMemory(proto.RootKey, rootKeyHandle);
            if (copyResult.IsErr)
            {
                rootKeyHandle.Dispose();
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                    copyResult.UnwrapErr().ToEcliptixProtocolFailure());
            }

            EcliptixProtocolConnection connection = new(connectId, proto, sendingStep, receivingStep, rootKeyHandle);

            sendingStep = null;
            receivingStep = null;
            rootKeyHandle = null;

            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Ok(connection);
        }
        catch (Exception ex)
        {
            sendingStep?.Dispose();
            receivingStep?.Dispose();
            rootKeyHandle?.Dispose();
            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to rehydrate connection from proto state.", ex));
        }
    }

    public Result<PublicKeyBundle, EcliptixProtocolFailure> GetPeerBundle()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            if (_peerBundle != null)
                return Result<PublicKeyBundle, EcliptixProtocolFailure>.Ok(_peerBundle);
            
            return Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Peer bundle has not been set."));
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> SetConnectionState(PubKeyExchangeState newState)
    {
        lock (_lock)
        {
            return CheckDisposed();
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> SetPeerBundle(PublicKeyBundle peerBundle)
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
            SodiumSecureMemoryHandle? tempRootHandle = null;
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

                Result<Unit, EcliptixProtocolFailure> keyValidation = ValidateInitialKeys(initialRootKey, initialPeerDhPublicKey);
                if (keyValidation.IsErr)
                    return keyValidation;

                peerDhPublicCopy = (byte[])initialPeerDhPublicKey.Clone();
                Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult = 
                    SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize);
                if (allocResult.IsErr)
                {
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(allocResult.UnwrapErr().ToEcliptixProtocolFailure());
                }

                tempRootHandle = allocResult.Unwrap();
                Result<Unit, SodiumFailure> writeResult = tempRootHandle.Write(initialRootKey);
                if (writeResult.IsErr)
                {
                    tempRootHandle.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr().ToEcliptixProtocolFailure());
                }

                try
                {
                    byte[] sendKeyBytes = new byte[Constants.X25519KeySize];
                    byte[] recvKeyBytes = new byte[Constants.X25519KeySize];

                    using (HkdfSha256 hkdfSend = new(initialRootKey, null))
                    {
                        hkdfSend.Expand(InitialSenderChainInfo, sendKeyBytes);
                    }

                    using (HkdfSha256 hkdfRecv = new(initialRootKey, null))
                    {
                        hkdfRecv.Expand(InitialReceiverChainInfo, recvKeyBytes);
                    }

                    if (_isInitiator)
                    {
                        senderChainKey = sendKeyBytes;
                        receiverChainKey = recvKeyBytes;
                    }
                    else
                    {
                        senderChainKey = recvKeyBytes;
                        receiverChainKey = sendKeyBytes;
                    }
                }
                catch (Exception ex)
                {
                    tempRootHandle?.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.DeriveKey("Failed to derive initial chain keys.", ex));
                }

                Result<byte[], SodiumFailure> persistentKeyReadResult = 
                    _persistentDhPrivateKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize);
                if (persistentKeyReadResult.IsErr)
                {
                    tempRootHandle?.Dispose();
                    WipeIfNotNull(peerDhPublicCopy);
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        persistentKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
                }
                persistentPrivKeyBytes = persistentKeyReadResult.Unwrap();

                Result<Unit, EcliptixProtocolFailure> updateResult = 
                    _sendingStep.UpdateKeysAfterDhRatchet(senderChainKey!);
                if (updateResult.IsErr)
                {
                    tempRootHandle?.Dispose();
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
                tempRootHandle = null;
                _receivingStep = createResult.Unwrap();
                _peerDhPublicKey = peerDhPublicCopy;
                peerDhPublicCopy = null;
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

    internal Result<(EcliptixMessageKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure>
        PrepareNextSendMessage()
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> expiredCheck = EnsureNotExpired();
            if (expiredCheck.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(expiredCheck.UnwrapErr());

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult = EnsureSendingStepInitialized();
            if (sendingStepResult.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());

            EcliptixProtocolChainStep sendingStep = sendingStepResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> ratchetResult = MaybePerformSendingDhRatchet(sendingStep);
            if (ratchetResult.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());

            bool includeDhKey = ratchetResult.Unwrap();

            Result<uint, EcliptixProtocolFailure> currentIndexResult = sendingStep.GetCurrentIndex();
            if (currentIndexResult.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

            uint currentIndex = currentIndexResult.Unwrap();

            Result<EcliptixMessageKey, EcliptixProtocolFailure> derivedKeyResult = 
                sendingStep.GetOrDeriveKeyFor(currentIndex + 1);
            if (derivedKeyResult.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(derivedKeyResult.UnwrapErr());

            EcliptixMessageKey derivedKey = derivedKeyResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> setIndexResult = sendingStep.SetCurrentIndex(currentIndex + 1);
            if (setIndexResult.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());

            EcliptixMessageKey originalKey = derivedKey;

            return SecureMemoryUtils.WithSecureBuffer(
                Constants.AesKeySize,
                keySpan =>
                {
                    originalKey.ReadKeyMaterial(keySpan);
                    byte[] keyArray = new byte[keySpan.Length];
                    keySpan.CopyTo(keyArray);
                    _sendingStep.PruneOldKeys();
                    
                    Result<EcliptixMessageKey, EcliptixProtocolFailure> clonedKeyResult = 
                        EcliptixMessageKey.New(originalKey.Index, keyArray);
                    if (clonedKeyResult.IsErr)
                        return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(clonedKeyResult.UnwrapErr());

                    return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Ok(
                        (clonedKeyResult.Unwrap(), includeDhKey));
                });
        }
    }

    internal Result<EcliptixMessageKey, EcliptixProtocolFailure> ProcessReceivedMessage(uint receivedIndex)
    {
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> expiredCheck = EnsureNotExpired();
            if (expiredCheck.IsErr)
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(expiredCheck.UnwrapErr());

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult = EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());

            EcliptixProtocolChainStep receivingStep = receivingStepResult.Unwrap();

            Result<EcliptixMessageKey, EcliptixProtocolFailure> derivedKeyResult = 
                receivingStep.GetOrDeriveKeyFor(receivedIndex);
            if (derivedKeyResult.IsErr)
                return derivedKeyResult;

            EcliptixMessageKey derivedKey = derivedKeyResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> setIndexResult = receivingStep.SetCurrentIndex(derivedKey.Index);
            if (setIndexResult.IsErr)
                return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Err(setIndexResult.UnwrapErr());

            _receivingStep!.PruneOldKeys();
            return Result<EcliptixMessageKey, EcliptixProtocolFailure>.Ok(derivedKey);
        }
    }

    public Result<Unit, EcliptixProtocolFailure> PerformReceivingRatchet(byte[] receivedDhKey)
    {
        lock (_lock)
        {
            return PerformDhRatchet(false, receivedDhKey);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformDhRatchet(bool isSender,
        byte[]? receivedDhPublicKeyBytes = null)
    {
        byte[]? dhSecret = null, newRootKey = null, newChainKeyForTargetStep = null, newEphemeralPublicKey = null;
        byte[]? localPrivateKeyBytes = null, currentRootKey = null, newDhPrivateKeyBytes = null;
        SodiumSecureMemoryHandle? newEphemeralSkHandle = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return disposedCheck;

            if (_rootKeyHandle is not { IsInvalid: false })
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Root key handle not initialized."));

            try
            {
                if (isSender)
                {
                    if (_sendingStep == null || _peerDhPublicKey == null)
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            EcliptixProtocolFailure.DeriveKey("Sender ratchet pre-conditions not met."));
                    
                    Result<(SodiumSecureMemoryHandle, byte[]), EcliptixProtocolFailure> ephResult =
                        SodiumInterop.GenerateX25519KeyPair("Ephemeral DH Ratchet");
                    if (ephResult.IsErr)
                        return ephResult.Map<Unit>(_ => Unit.Value);
                    
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
                            EcliptixProtocolFailure.DeriveKey("Receiver ratchet pre-conditions not met."));
                    
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
                    EcliptixProtocolFailure.DeriveKey("DH calculation failed during ratchet.", ex));
            }

            Result<byte[], SodiumFailure> rootKeyReadResult = _rootKeyHandle!.ReadBytes(Constants.X25519KeySize);
            if (rootKeyReadResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    rootKeyReadResult.UnwrapErr().ToEcliptixProtocolFailure());
            currentRootKey = rootKeyReadResult.Unwrap();
            
            using SecurePooledArray<byte> hkdfOutputBuffer = SecureArrayPool.Rent<byte>(Constants.X25519KeySize * 2);
            Span<byte> hkdfOutputSpan = hkdfOutputBuffer.AsSpan();

            using (HkdfSha256 hkdf = new(dhSecret!, currentRootKey))
            {
                hkdf.Expand(DhRatchetInfo, hkdfOutputSpan);
            }

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

            _receivedNewDhKey = false;

            if (_eventHandler != null)
            {
                uint newIndex = isSender
                    ? _sendingStep.GetCurrentIndex().UnwrapOr(0)
                    : _receivingStep!.GetCurrentIndex().UnwrapOr(0);
                _eventHandler.OnDhRatchetPerformed(_id, isSender, newIndex);
            }

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
        lock (_lock)
        {
            Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
            if (disposedCheck.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

            Span<byte> nonceBuffer = stackalloc byte[AesGcmNonceSize];
            RandomNumberGenerator.Fill(nonceBuffer[..8]);
            uint currentNonce = (uint)Interlocked.Increment(ref _nonceCounter) - 1;
            BinaryPrimitives.WriteUInt32LittleEndian(nonceBuffer[8..], currentNonce);
            
            byte[] result = new byte[AesGcmNonceSize];
            nonceBuffer.CopyTo(result);
            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
        }
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

            return stepResult.Unwrap().ReadDhPublicKey();
        }
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

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> receivingStepResult = EnsureReceivingStepInitialized();
            if (receivingStepResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(receivingStepResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> receivingSkipResult = 
                receivingStepResult.Unwrap().SkipKeysUntil(remoteSendingChainLength);
            if (receivingSkipResult.IsErr)
                return receivingSkipResult;

            Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> sendingStepResult = EnsureSendingStepInitialized();
            if (sendingStepResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(sendingStepResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> sendingSkipResult = 
                sendingStepResult.Unwrap().SkipKeysUntil(remoteReceivingChainLength);
            if (sendingSkipResult.IsErr)
                return sendingSkipResult;

            _eventHandler!.OnChainSynchronized(_id,
                _sendingStep?.GetCurrentIndex().UnwrapOr(0) ?? 0,
                _receivingStep?.GetCurrentIndex().UnwrapOr(0) ?? 0);

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
    }

    ~EcliptixProtocolConnection()
    {
        Dispose(false);
    }

    private void SecureCleanupLogic()
    {
        _rootKeyHandle?.Dispose();
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
            SodiumInterop.SecureWipe(data).IgnoreResult();
        }
    }

    private Result<Unit, EcliptixProtocolFailure> EnsureNotExpired()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return disposedCheck;

        if (DateTimeOffset.UtcNow - _createdAt > SessionTimeout)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic($"Session {_id} has expired."));
        
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
            EcliptixProtocolFailure.Generic("Sending chain step not initialized."));
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureReceivingStepInitialized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(disposedCheck.UnwrapErr());

        if (_receivingStep != null)
            return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_receivingStep);
        
        return Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("Receiving chain step not initialized."));
    }

    private Result<Unit, EcliptixProtocolFailure> CheckIfNotFinalized()
    {
        Result<Unit, EcliptixProtocolFailure> disposedCheck = CheckDisposed();
        if (disposedCheck.IsErr)
            return disposedCheck;

        if (_rootKeyHandle != null || _receivingStep != null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Session has already been finalized."));
        
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<Unit, EcliptixProtocolFailure> ValidateInitialKeys(byte[] rootKey, byte[] peerDhKey)
    {
        if (rootKey.Length != Constants.X25519KeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput($"Initial root key must be {Constants.X25519KeySize} bytes."));
        if (peerDhKey.Length != Constants.X25519PublicKeySize)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"Initial peer DH public key must be {Constants.X25519PublicKeySize} bytes."));
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<bool, EcliptixProtocolFailure> MaybePerformSendingDhRatchet(EcliptixProtocolChainStep sendingStep)
    {
        Result<uint, EcliptixProtocolFailure> currentIndexResult = sendingStep.GetCurrentIndex();
        if (currentIndexResult.IsErr)
            return Result<bool, EcliptixProtocolFailure>.Err(currentIndexResult.UnwrapErr());

        uint currentIndex = currentIndexResult.Unwrap();
        bool shouldRatchet = (currentIndex + 1) % DhRotationInterval == 0 || _receivedNewDhKey;
        
        if (shouldRatchet)
        {
            Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformDhRatchet(true);
            if (ratchetResult.IsErr)
                return Result<bool, EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());

            _receivedNewDhKey = false;
            return Result<bool, EcliptixProtocolFailure>.Ok(true);
        }
        
        return Result<bool, EcliptixProtocolFailure>.Ok(false);
    }
}