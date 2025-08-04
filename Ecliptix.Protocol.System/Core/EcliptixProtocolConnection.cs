using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Sodium;

namespace Ecliptix.Protocol.System.Core;

public sealed class EcliptixProtocolConnection : IDisposable
{
    public static readonly TimeSpan LiveConnectTimeout = TimeSpan.FromHours(24);
    
    private const int DhRotationInterval = 10;
    private const int AesGcmNonceSize = 12;
    
    private DateTimeOffset _lastActivityAt;

    private static readonly byte[] InitialSenderChainInfo = "ShieldInitSend"u8.ToArray();
    private static readonly byte[] InitialReceiverChainInfo = "ShieldInitRecv"u8.ToArray();
    private static readonly byte[] DhRatchetInfo = "ShieldDhRatchet"u8.ToArray();

    private readonly Lock _lock = new();

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

        _peerDhPublicKey = null;
        _receivedNewDhKey = false;
        _isFirstReceivingRatchet = true;  // Default to true for first ratchet
        _disposed = false;
        _lastActivityAt = DateTimeOffset.UtcNow;
    }

    private EcliptixProtocolConnection(uint id, RatchetState proto, EcliptixProtocolChainStep sendingStep,
        EcliptixProtocolChainStep? receivingStep, SodiumSecureMemoryHandle rootKeyHandle)
    {
        _id = id;
        _isInitiator = proto.IsInitiator;
        _lastActivityAt = proto.LastActivityAt.ToDateTimeOffset();
        _nonceCounter = proto.NonceCounter;
        _peerBundle = PublicKeyBundle.FromProtobufExchange(proto.PeerBundle).Unwrap();
        _peerDhPublicKey = proto.PeerDhPublicKey.IsEmpty ? null : proto.PeerDhPublicKey.ToByteArray();
        _isFirstReceivingRatchet = proto.IsFirstReceivingRatchet;  // Restore from proto
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
                    LastActivityAt = Timestamp.FromDateTimeOffset(_lastActivityAt),
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
            return CheckDisposed().Bind(_ =>
                _peerBundle != null
                    ? Result<PublicKeyBundle, EcliptixProtocolFailure>.Ok(_peerBundle)
                    : Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic("Peer bundle has not been set.")));
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
            byte[]? localSenderCk = null,
                localReceiverCk = null,
                persistentPrivKeyBytes = null,
                peerDhPublicCopy = null;

            try
            {
                return CheckDisposed()
                    .Bind(_ => CheckTimeout())
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
                        localSenderCk = ArrayPool<byte>.Shared.Rent(Constants.X25519KeySize);
                        localReceiverCk = ArrayPool<byte>.Shared.Rent(Constants.X25519KeySize);
                        return DeriveInitialChainKeys(initialRootKey, localSenderCk, localReceiverCk);
                    })
                    .Bind(_ => _persistentDhPrivateKeyHandle!.ReadBytes(Constants.X25519PrivateKeySize)
                        .MapSodiumFailure()
                        .Map(bytes =>
                        {
                            persistentPrivKeyBytes = bytes;
                            return Unit.Value;
                        }))
                    .Bind(_ => _sendingStep.UpdateKeysAfterDhRatchet(localSenderCk.AsSpan(0, Constants.X25519KeySize)
                        .ToArray()))
                    .Bind(_ => EcliptixProtocolChainStep.Create(ChainStepType.Receiver,
                        localReceiverCk.AsSpan(0, Constants.X25519KeySize).ToArray(), null, null))  // Null DH for receiver
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
                if (localSenderCk != null) ArrayPool<byte>.Shared.Return(localSenderCk, clearArray: true);
                if (localReceiverCk != null) ArrayPool<byte>.Shared.Return(localReceiverCk, clearArray: true);
                WipeIfNotNull(persistentPrivKeyBytes);
            }
        }
    }

    internal Result<(EcliptixMessageKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure>
        PrepareNextSendMessage()
    {
        lock (_lock)
        {
            UpdateLastActivityTime();

            byte[]? keyMaterial = null;

            try
            {
                return CheckDisposed()
                    .Bind(_ => CheckTimeout())
                    .Bind(_ => MaybePerformSendingDhRatchet(_sendingStep)
                        .Bind(includeDhKey => _sendingStep.GetCurrentIndex()
                            .Bind(currentIndex => _sendingStep.GetOrDeriveKeyFor(currentIndex + 1)
                                .Bind(derivedKey =>
                                    _sendingStep.SetCurrentIndex(currentIndex + 1)
                                        .Map(_ => (derivedKey, includeDhKey))))))
                    .Bind(result =>
                    {
                        (EcliptixMessageKey originalKey, bool includeDhKey) = result;
                        keyMaterial = ArrayPool<byte>.Shared.Rent(Constants.AesKeySize);
                        Span<byte> keySpan = keyMaterial.AsSpan(0, Constants.AesKeySize);
                        originalKey.ReadKeyMaterial(keySpan);

                        return EcliptixMessageKey.New(originalKey.Index, keySpan)
                            .Map(clonedKey => (clonedKey, includeDhKey));
                    })
                    .Map(finalResult =>
                    {
                        _sendingStep.PruneOldKeys();
                        return finalResult;
                    });
            }
            finally
            {
                if (keyMaterial != null) ArrayPool<byte>.Shared.Return(keyMaterial, clearArray: true);
            }
        }
    }

    internal Result<EcliptixMessageKey, EcliptixProtocolFailure> ProcessReceivedMessage(uint receivedIndex,
        byte[]? receivedDhPublicKeyBytes)
    {
        lock (_lock)
        {
            UpdateLastActivityTime();

            return CheckDisposed()
                .Bind(_ => CheckTimeout())
                .Bind(_ => MaybePerformReceivingDhRatchet(_receivingStep!, receivedDhPublicKeyBytes)
                    .Bind(_ => _receivingStep!.GetOrDeriveKeyFor(receivedIndex))
                    .Bind(derivedKey => _receivingStep!.SetCurrentIndex(derivedKey.Index).Map(_ => derivedKey)))
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
        byte[]? hkdfOutput = null;
        SodiumSecureMemoryHandle? newEphemeralSkHandle = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> initialCheck = CheckDisposed().Bind(_ =>
                _rootKeyHandle is { IsInvalid: false }
                    ? Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value)
                    : Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic("Root key handle not initialized.")));
            if (initialCheck.IsErr) return initialCheck;

            // Add pub validation for receivedDhPublicKeyBytes if receiver
            if (!isSender && receivedDhPublicKeyBytes != null && !IsValidX25519PublicKey(receivedDhPublicKeyBytes)) {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput("Invalid received DH public key (not on curve)."));
            }

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
            hkdfOutput = ArrayPool<byte>.Shared.Rent(Constants.X25519KeySize * 2);
            using (HkdfSha256 hkdf = new(dhSecret!, currentRootKey))
            {
                hkdf.Expand(DhRatchetInfo, hkdfOutput.AsSpan(0, Constants.X25519KeySize * 2));
            }

            newRootKey = hkdfOutput.AsSpan(0, Constants.X25519KeySize).ToArray();
            newChainKeyForTargetStep = hkdfOutput.AsSpan(Constants.X25519KeySize).ToArray();

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
                    _peerDhPublicKey = (byte[])receivedDhPublicKeyBytes!.Clone(); 

                    _receivedNewDhKey = true; 
                }
            }

            if (updateResult.IsErr) return updateResult;

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
            if (hkdfOutput != null) ArrayPool<byte>.Shared.Return(hkdfOutput, clearArray: true);
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
                RandomNumberGenerator.Fill(nonceBuffer[..4]);  
                ulong currentNonce = Interlocked.Increment(ref _nonceCounter) - 1;
                BinaryPrimitives.WriteUInt64LittleEndian(nonceBuffer[4..], currentNonce);
                return nonceBuffer.ToArray();
            });
        }
    }

    public Result<byte[]?, EcliptixProtocolFailure> GetCurrentSenderDhPublicKey()
    {
        lock (_lock)
        {
            return _sendingStep.ReadDhPublicKey();
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
            return CheckDisposed()
                .Bind(_ => CheckTimeout())
                .Bind(_ => _receivingStep!.SkipKeysUntil(remoteSendingChainLength))
                .Bind(_ => _sendingStep.SkipKeysUntil(remoteReceivingChainLength));
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

    private Result<Unit, EcliptixProtocolFailure> CheckIfNotFinalized()
    {
        return CheckDisposed().Bind(_ =>
            _rootKeyHandle != null || _receivingStep != null
                ? Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Session has already been finalized."))
                : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value));
    }

    private Result<Unit, EcliptixProtocolFailure> CheckTimeout()
    {
        if (DateTimeOffset.UtcNow - _lastActivityAt > LiveConnectTimeout) {
            return Result<Unit, EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.Generic("Session expired."));
        }
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

    private Result<Unit, EcliptixProtocolFailure> DeriveInitialChainKeys(byte[] rootKey, byte[] senderCkDest,
        byte[] receiverCkDest)
    {
        return Result<Unit, EcliptixProtocolFailure>.Try(() =>
        {
            Span<byte> sendSpan = stackalloc byte[Constants.X25519KeySize];
            Span<byte> recvSpan = stackalloc byte[Constants.X25519KeySize];
            using (HkdfSha256 hkdfSend = new(rootKey, null))
            {
                hkdfSend.Expand(InitialSenderChainInfo, sendSpan);
            }

            using (HkdfSha256 hkdfRecv = new(rootKey, null))
            {
                hkdfRecv.Expand(InitialReceiverChainInfo, recvSpan);
            }

            if (_isInitiator)
            {
                sendSpan.CopyTo(senderCkDest);
                recvSpan.CopyTo(receiverCkDest);
            }
            else
            {
                recvSpan.CopyTo(senderCkDest);
                sendSpan.CopyTo(receiverCkDest);
            }
        }, ex => EcliptixProtocolFailure.DeriveKey("Failed to derive initial chain keys.", ex));
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

    private void UpdateLastActivityTime()
    {
        _lastActivityAt = DateTimeOffset.UtcNow;
    }

    private Result<Unit, EcliptixProtocolFailure> MaybePerformReceivingDhRatchet(
        EcliptixProtocolChainStep receivingStep, byte[]? receivedDhPublicKeyBytes)
    {
        if (receivedDhPublicKeyBytes == null) return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        bool keysDiffer = _peerDhPublicKey == null || !receivedDhPublicKeyBytes.SequenceEqual(_peerDhPublicKey);
        if (!keysDiffer) return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        bool shouldRatchetNow = _isFirstReceivingRatchet || true; 
        if (shouldRatchetNow)
        {
            _isFirstReceivingRatchet = false;
            return PerformDhRatchet(false, receivedDhPublicKeyBytes);
        }

        _peerDhPublicKey = (byte[])receivedDhPublicKeyBytes.Clone(); 
        _receivedNewDhKey = true;

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static bool IsValidX25519PublicKey(byte[] pk)
    {
        if (pk.All(b => b == 0)) return false;
        byte[] clamped = (byte[])pk.Clone();
        clamped[0] &= 248;
        clamped[31] = (byte)((clamped[31] & 127) | 64);
        return true; 
    }
}
