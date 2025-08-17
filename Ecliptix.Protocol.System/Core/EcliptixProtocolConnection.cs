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
    private bool _isFirstReceivingRatchet;
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
        _peerDhPublicKey = proto.PeerDhPublicKey.IsEmpty ? null : proto.PeerDhPublicKey.ToByteArray();
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
            Result<EcliptixProtocolConnection, EcliptixProtocolFailure> overallResult =
                SodiumInterop.GenerateX25519KeyPair("Initial Sending DH")
                    .Bind(initialSendKeys =>
                    {
                        (initialSendingDhPrivateKeyHandle, initialSendingDhPublicKey) = initialSendKeys;

                        return initialSendingDhPrivateKeyHandle.ReadBytes(Constants.X25519PrivateKeySize)
                            .MapSodiumFailure()
                            .Map(bytes =>
                            {
                                initialSendingDhPrivateKeyBytes = bytes;
                                return Unit.Value;
                            });
                    })
                    .Bind(_ => SodiumInterop.GenerateX25519KeyPair("Persistent DH"))
                    .Bind(persistentKeys =>
                    {
                        (persistentDhPrivateKeyHandle, persistentDhPublicKey) = persistentKeys;

                        byte[] tempChainKey = new byte[Constants.X25519KeySize];
                        Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> stepResult =
                            EcliptixProtocolChainStep.Create(ChainStepType.Sender, tempChainKey,
                                initialSendingDhPrivateKeyBytes, initialSendingDhPublicKey);
                        WipeIfNotNull(tempChainKey);
                        WipeIfNotNull(initialSendingDhPrivateKeyBytes);
                        initialSendingDhPrivateKeyBytes = null;
                        return stepResult;
                    })
                    .Bind(createdSendingStep =>
                    {
                        sendingStep = createdSendingStep;
                        EcliptixProtocolConnection connection = new(connectId, isInitiator,
                            initialSendingDhPrivateKeyHandle!, sendingStep, persistentDhPrivateKeyHandle!,
                            persistentDhPublicKey!);
                        initialSendingDhPrivateKeyHandle = null;
                        persistentDhPrivateKeyHandle = null;
                        sendingStep = null;
                        return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Ok(connection);
                    });

            if (overallResult.IsErr)
            {
                initialSendingDhPrivateKeyHandle?.Dispose();
                sendingStep?.Dispose();
                persistentDhPrivateKeyHandle?.Dispose();
                WipeIfNotNull(initialSendingDhPrivateKeyBytes);
            }

            return overallResult;
        }
        catch (Exception ex)
        {
            initialSendingDhPrivateKeyHandle?.Dispose();
            sendingStep?.Dispose();
            persistentDhPrivateKeyHandle?.Dispose();
            WipeIfNotNull(initialSendingDhPrivateKeyBytes);
            Console.WriteLine($"[EcliptixProtocolConnection] Error creating connection: {ex.Message}");
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

                Result<byte[], EcliptixProtocolFailure> rootKeyResult =
                    _rootKeyHandle!.ReadBytes(Constants.X25519KeySize).MapSodiumFailure();
                if (rootKeyResult.IsErr)
                    return Result<RatchetState, EcliptixProtocolFailure>.Err(rootKeyResult.UnwrapErr());

                RatchetState proto = new()
                {
                    IsInitiator = _isInitiator,
                    CreatedAt = Timestamp.FromDateTimeOffset(_createdAt),
                    NonceCounter = _nonceCounter,
                    PeerBundle = _peerBundle!.ToProtobufExchange(),
                    PeerDhPublicKey = ByteString.CopyFrom(_peerDhPublicKey ?? []),
                    IsFirstReceivingRatchet = _isFirstReceivingRatchet,
                    RootKey = ByteString.CopyFrom(rootKeyResult.Unwrap()),
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
                Console.WriteLine($"[EcliptixProtocolConnection] Error exporting to proto state: {ex.Message}");
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

            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> rootKeyResult =
                SodiumSecureMemoryHandle.Allocate(proto.RootKey.Length).MapSodiumFailure();
            if (rootKeyResult.IsErr)
                return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(rootKeyResult.UnwrapErr());
            rootKeyHandle = rootKeyResult.Unwrap();
            rootKeyHandle.Write(proto.RootKey.ToByteArray()).MapSodiumFailure().Unwrap();

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
            Console.WriteLine($"[EcliptixProtocolConnection] Error restoring from proto state: {ex.Message}");
            return Result<EcliptixProtocolConnection, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to rehydrate connection from proto state.", ex));
        }
    }

    public Result<PublicKeyBundle, EcliptixProtocolFailure> GetPeerBundle()
    {
        lock (_lock)
        {
            return CheckDisposed().Bind(_ =>
                _peerBundle != null
                    ? Result<PublicKeyBundle, EcliptixProtocolFailure>.Ok(_peerBundle)
                    : Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic("Peer bundle has not been set.")));
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> SetConnectionState(PubKeyExchangeState newState)
    {
        lock (_lock)
        {
            return CheckDisposed().Map(u => u);
        }
    }

    internal Result<Unit, EcliptixProtocolFailure> SetPeerBundle(PublicKeyBundle peerBundle)
    {
        lock (_lock)
        {
            return CheckDisposed().Bind(_ =>
            {
                _peerBundle = peerBundle;
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            });
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
                return CheckDisposed()
                    .Bind(_ => CheckIfNotFinalized())
                    .Bind(_ => ValidateInitialKeys(initialRootKey, initialPeerDhPublicKey))
                    .Bind(_ =>
                    {
                        peerDhPublicCopy = (byte[])initialPeerDhPublicKey.Clone();
                        return SodiumSecureMemoryHandle.Allocate(Constants.X25519KeySize).MapSodiumFailure()
                            .Bind(handle =>
                            {
                                tempRootHandle = handle;
                                return handle.Write(initialRootKey).MapSodiumFailure();
                            });
                    })
            .Bind(_ =>
            {
                return Result<Unit, EcliptixProtocolFailure>.Try(() =>
                {
                    byte[] sendKeyBytes = new byte[Constants.X25519KeySize];
                    byte[] recvKeyBytes = new byte[Constants.X25519KeySize];

                    Console.WriteLine($"[DESKTOP] DeriveInitialChainKeys (keys hidden for security)");
                    using (HkdfSha256 hkdfSend = new(initialRootKey, null))
                    {
                        hkdfSend.Expand(InitialSenderChainInfo, sendKeyBytes);
                    }

                    using (HkdfSha256 hkdfRecv = new(initialRootKey, null))
                    {
                        hkdfRecv.Expand(InitialReceiverChainInfo, recvKeyBytes);
                    }
                    Console.WriteLine($"[DESKTOP] HKDF key derivation completed (keys hidden for security)");
                    Console.WriteLine($"[DESKTOP] Is Initiator: {_isInitiator}");

                    if (_isInitiator)
                    {
                        senderChainKey = sendKeyBytes;
                        receiverChainKey = recvKeyBytes;
                        Console.WriteLine($"[DESKTOP] As initiator - Chain keys established (hidden for security)");
                    }
                    else
                    {
                        senderChainKey = recvKeyBytes;
                        receiverChainKey = sendKeyBytes;
                        Console.WriteLine($"[DESKTOP] As responder - Chain keys established (hidden for security)");
                    }
                    return Unit.Value;
                }, ex => EcliptixProtocolFailure.DeriveKey("Failed to derive initial chain keys.", ex));
            })
                    .Bind(_ => _persistentDhPrivateKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize)
                        .MapSodiumFailure()
                        .Map(bytes =>
                        {
                            persistentPrivKeyBytes = bytes;
                            return Unit.Value;
                        }))
            .Bind(_ =>
            {
                Console.WriteLine($"[DESKTOP] Updating sender chain keys after DH ratchet");
                return _sendingStep.UpdateKeysAfterDhRatchet(senderChainKey!);
            })
            .Bind(_ =>
            {
                Console.WriteLine($"[DESKTOP] Creating receiver step with new chain key");
                return EcliptixProtocolChainStep.Create(ChainStepType.Receiver,
                    receiverChainKey!, persistentPrivKeyBytes, _persistentDhPublicKey);
            })
            .Map(receivingStep =>
            {
                _rootKeyHandle = tempRootHandle;
                tempRootHandle = null;
                _receivingStep = receivingStep;
                _peerDhPublicKey = peerDhPublicCopy;
                peerDhPublicCopy = null;
                return Unit.Value;
            })
            .MapErr(err =>
            {
                tempRootHandle?.Dispose();
                return err;
            });
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
            Result<(EcliptixMessageKey derivedKey, bool includeDhKey), EcliptixProtocolFailure> operationsResult = CheckDisposed()
                .Bind(_ => EnsureNotExpired())
                .Bind(_ => EnsureSendingStepInitialized())
                .Bind(sendingStep => MaybePerformSendingDhRatchet(sendingStep)
                    .Bind(includeDhKey => sendingStep.GetCurrentIndex()
                        .Bind(currentIndex => sendingStep.GetOrDeriveKeyFor(currentIndex + 1)
                            .Bind(derivedKey =>
                                sendingStep.SetCurrentIndex(currentIndex + 1)
                                    .Map(_ => (derivedKey, includeDhKey))))));

            if (operationsResult.IsErr)
                return Result<(EcliptixMessageKey, bool), EcliptixProtocolFailure>.Err(operationsResult.UnwrapErr());

            (EcliptixMessageKey originalKey, bool includeDhKey) = operationsResult.Unwrap();

            return SecureMemoryUtils.WithSecureBuffer(
                Constants.AesKeySize,
                keySpan =>
                {
                    originalKey.ReadKeyMaterial(keySpan);
                    byte[] keyArray = keySpan.ToArray();
                    _sendingStep.PruneOldKeys();
                    return EcliptixMessageKey.New(originalKey.Index, keyArray)
                        .Map(clonedKey => (clonedKey, includeDhKey));
                });
        }
    }

    internal Result<EcliptixMessageKey, EcliptixProtocolFailure> ProcessReceivedMessage(uint receivedIndex)
    {
        lock (_lock)
        {
            return CheckDisposed()
                .Bind(_ => EnsureNotExpired())
                .Bind(_ => EnsureReceivingStepInitialized())
                .Bind(receivingStep => receivingStep.GetOrDeriveKeyFor(receivedIndex)
                    .Bind(derivedKey => receivingStep.SetCurrentIndex(derivedKey.Index).Map(_ => derivedKey)))
                .Map(finalKey =>
                {
                    _receivingStep!.PruneOldKeys();
                    return finalKey;
                });
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
            Result<Unit, EcliptixProtocolFailure> initialCheck = CheckDisposed().Bind(_ =>
                _rootKeyHandle is { IsInvalid: false }
                    ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
                    : Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic("Root key handle not initialized.")));
            if (initialCheck.IsErr) return initialCheck;

            Result<Unit, EcliptixProtocolFailure> dhCalculationResult = Result<Unit, EcliptixProtocolFailure>.Try(() =>
            {
                if (isSender)
                {
                    if (_sendingStep == null || _peerDhPublicKey == null)
                        throw new InvalidOperationException("Sender ratchet pre-conditions not met.");
                    (SodiumSecureMemoryHandle skHandle, byte[] pk) ephResult =
                        SodiumInterop.GenerateX25519KeyPair("Ephemeral DH Ratchet").Unwrap();
                    (newEphemeralSkHandle, newEphemeralPublicKey) = ephResult;
                    localPrivateKeyBytes = newEphemeralSkHandle.ReadBytes(Constants.X25519PrivateKeySize).Unwrap();
                    dhSecret = ScalarMult.Mult(localPrivateKeyBytes, _peerDhPublicKey);
                }
                else
                {
                    if (_receivingStep == null || receivedDhPublicKeyBytes is not
                        { Length: Constants.X25519PublicKeySize })
                        throw new InvalidOperationException("Receiver ratchet pre-conditions not met.");
                    localPrivateKeyBytes = _currentSendingDhPrivateKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize)
                        .Unwrap();
                    dhSecret = ScalarMult.Mult(localPrivateKeyBytes, receivedDhPublicKeyBytes);
                }
            }, ex => EcliptixProtocolFailure.DeriveKey("DH calculation failed during ratchet.", ex));
            if (dhCalculationResult.IsErr) return dhCalculationResult;

            currentRootKey = _rootKeyHandle!.ReadBytes(Constants.X25519KeySize).Unwrap();
            using SecurePooledArray<byte> hkdfOutputBuffer = SecureArrayPool.Rent<byte>(Constants.X25519KeySize * 2);
            Span<byte> hkdfOutputSpan = hkdfOutputBuffer.AsSpan();

            using (HkdfSha256 hkdf = new(dhSecret!, currentRootKey))
            {
                hkdf.Expand(DhRatchetInfo, hkdfOutputSpan);
            }

            newRootKey = hkdfOutputSpan[..Constants.X25519KeySize].ToArray();
            newChainKeyForTargetStep = hkdfOutputSpan[Constants.X25519KeySize..].ToArray();

            Result<Unit, EcliptixProtocolFailure> writeResult = _rootKeyHandle.Write(newRootKey).MapSodiumFailure();
            if (writeResult.IsErr) return writeResult.MapErr(f => f);

            Result<Unit, EcliptixProtocolFailure> updateResult;
            if (isSender)
            {
                newDhPrivateKeyBytes = newEphemeralSkHandle!.ReadBytes(Constants.X25519PrivateKeySize).Unwrap();
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
            return CheckDisposed().Map(_ =>
            {
                Span<byte> nonceBuffer = stackalloc byte[AesGcmNonceSize];
                RandomNumberGenerator.Fill(nonceBuffer[..8]);
                uint currentNonce = (uint)Interlocked.Increment(ref _nonceCounter) - 1;
                BinaryPrimitives.WriteUInt32LittleEndian(nonceBuffer[8..], currentNonce);
                return nonceBuffer.ToArray();
            });
        }
    }

    public Result<byte[]?, EcliptixProtocolFailure> GetCurrentPeerDhPublicKey()
    {
        lock (_lock)
        {
            return CheckDisposed().Map(_ => _peerDhPublicKey != null ? (byte[])_peerDhPublicKey.Clone() : null);
        }
    }

    public Result<byte[]?, EcliptixProtocolFailure> GetCurrentSenderDhPublicKey()
    {
        lock (_lock)
        {
            return CheckDisposed().Bind(_ => EnsureSendingStepInitialized()).Bind(step => step.ReadDhPublicKey());
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
            Console.WriteLine($"Syncing: remoteSending={remoteSendingChainLength}, remoteReceiving={remoteReceivingChainLength}");
            Result<Unit, EcliptixProtocolFailure> result = CheckDisposed()
                .Bind(_ => EnsureReceivingStepInitialized())
                .Bind(receivingStep =>
                {
                    Console.WriteLine($"Receiving chain current index: {receivingStep.GetCurrentIndex().Unwrap()}");
                    return receivingStep.SkipKeysUntil(remoteSendingChainLength);
                })
                .Bind(_ => EnsureSendingStepInitialized())
                .Bind(sendingStep =>
                {
                    Console.WriteLine($"Sending chain current index: {sendingStep.GetCurrentIndex().Unwrap()}");
                    return sendingStep.SkipKeysUntil(remoteReceivingChainLength);
                });

            if (result.IsOk)
            {
                _eventHandler!.OnChainSynchronized(_id,
                    _sendingStep?.GetCurrentIndex().UnwrapOr(0) ?? 0,
                    _receivingStep?.GetCurrentIndex().UnwrapOr(0) ?? 0);
            }

            return result;
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
            SodiumInterop.SecureWipe(data).MapSodiumFailure();
        }
    }

    private Result<Unit, EcliptixProtocolFailure> EnsureNotExpired()
    {
        return CheckDisposed().Bind(_ =>
            DateTimeOffset.UtcNow - _createdAt > SessionTimeout
                ? Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic($"Session {_id} has expired."))
                : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value));
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureSendingStepInitialized()
    {
        return CheckDisposed().Bind(_ =>
            _sendingStep != null
                ? Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_sendingStep)
                : Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Sending chain step not initialized.")));
    }

    private Result<EcliptixProtocolChainStep, EcliptixProtocolFailure> EnsureReceivingStepInitialized()
    {
        return CheckDisposed().Bind(_ =>
            _receivingStep != null
                ? Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Ok(_receivingStep)
                : Result<EcliptixProtocolChainStep, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Receiving chain step not initialized.")));
    }

    private Result<Unit, EcliptixProtocolFailure> CheckIfNotFinalized()
    {
        return CheckDisposed().Bind(_ =>
            _rootKeyHandle != null || _receivingStep != null
                ? Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Session has already been finalized."))
                : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value));
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
        return sendingStep.GetCurrentIndex().Bind(currentIndex =>
        {
            bool shouldRatchet = (currentIndex + 1) % DhRotationInterval == 0 || _receivedNewDhKey;
            if (shouldRatchet)
                return PerformDhRatchet(true).Map(_ =>
                {
                    _receivedNewDhKey = false;
                    return true;
                });
            return Result<bool, EcliptixProtocolFailure>.Ok(false);
        });
    }
}