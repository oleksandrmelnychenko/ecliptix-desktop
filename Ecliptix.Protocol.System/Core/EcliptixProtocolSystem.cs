using System.Diagnostics;
using System.Security.Cryptography;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Protocol.System.Core;

public class EcliptixProtocolSystem : IDisposable
{
    private readonly Lock _lock = new();
    private readonly EcliptixSystemIdentityKeys _ecliptixSystemIdentityKeys;
    
    private readonly AdaptiveRatchetManager _ratchetManager;
    private readonly ProtocolMetricsCollector _metricsCollector = new(TimeSpan.FromSeconds(30));
    private EcliptixProtocolConnection? _protocolConnection;
    private IProtocolEventHandler? _eventHandler;

    public EcliptixProtocolSystem(EcliptixSystemIdentityKeys ecliptixSystemIdentityKeys, RatchetConfig customConfig)
    {
        _ecliptixSystemIdentityKeys = ecliptixSystemIdentityKeys;
        _ratchetManager = new AdaptiveRatchetManager(customConfig);
        Log.Information("[CLIENT-PROTOCOL] Initialized with custom config - DH interval: {Interval}",
            customConfig.DhRatchetEveryNMessages);
    }

    public EcliptixSystemIdentityKeys GetIdentityKeys() => _ecliptixSystemIdentityKeys;

    public void SetEventHandler(IProtocolEventHandler? handler)
    {
        EcliptixProtocolConnection? connectionToUpdate;

        lock (_lock)
        {
            _eventHandler = handler;
            connectionToUpdate = _protocolConnection;
        }

        connectionToUpdate?.SetEventHandler(handler);
    }

    public void Dispose()
    {
        EcliptixProtocolConnection? connectionToDispose;

        lock (_lock)
        {
            connectionToDispose = _protocolConnection;
            _protocolConnection = null;
        }

        connectionToDispose?.Dispose();
        _ratchetManager.Dispose();
        _metricsCollector.Dispose();
        GC.SuppressFinalize(this);
    }

    private EcliptixProtocolConnection? GetConnectionSafe()
    {
        lock (_lock)
        {
            return _protocolConnection;
        }
    }

    private static Timestamp GetProtoTimestamp()
    {
        return Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow);
    }

    public Result<PubKeyExchange, EcliptixProtocolFailure> BeginDataCenterPubKeyExchange(
        uint connectId,
        PubKeyExchangeType exchangeType)
    {
        _ecliptixSystemIdentityKeys.GenerateEphemeralKeyPair();

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> bundleResult = _ecliptixSystemIdentityKeys.CreatePublicBundle();
        if (bundleResult.IsErr)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());

        LocalPublicKeyBundle bundle = bundleResult.Unwrap();

        RatchetConfig configToUse = GetConfigForExchangeType(exchangeType);

        Log.Information("🔧 KEY-EXCHANGE: Creating protocol connection for {ExchangeType} connectId {ConnectId} with config DH every {Messages} messages", 
            exchangeType, connectId, configToUse.DhRatchetEveryNMessages);
        
        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> sessionResult =
            EcliptixProtocolConnection.Create(connectId, true, configToUse);
        if (sessionResult.IsErr)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(sessionResult.UnwrapErr());

        EcliptixProtocolConnection session = sessionResult.Unwrap();

        IProtocolEventHandler? currentHandler;
        lock (_lock)
        {
            _protocolConnection = session;
            currentHandler = _eventHandler;
        }

        session.SetEventHandler(currentHandler);

        Result<byte[]?, EcliptixProtocolFailure> dhKeyResult = session.GetCurrentSenderDhPublicKey();
        if (dhKeyResult.IsErr)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());

        byte[]? dhPublicKey = dhKeyResult.Unwrap();
        if (dhPublicKey == null)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PrepareLocal("DH public key is null"));

        PubKeyExchange pubKeyExchange = new()
        {
            State = PubKeyExchangeState.Init,
            OfType = exchangeType,
            Payload = bundle.ToProtobufExchange().ToByteString(),
            InitialDhPublicKey = ByteString.CopyFrom(dhPublicKey)
        };

        return Result<PubKeyExchange, EcliptixProtocolFailure>.Ok(pubKeyExchange);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteDataCenterPubKeyExchange(PubKeyExchange peerMessage)
    {
        Result<byte[]?, EcliptixProtocolFailure> ourDhKeyResult = _protocolConnection?.GetCurrentSenderDhPublicKey() ??
                                                                  Result<byte[]?, EcliptixProtocolFailure>.Err(
                                                                      EcliptixProtocolFailure.Generic("No connection"));
        if (ourDhKeyResult.IsOk)
        {
            byte[]? ourDhKey = ourDhKeyResult.Unwrap();
            if (ourDhKey != null && peerMessage.InitialDhPublicKey.ToArray().AsSpan().SequenceEqual(ourDhKey))
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Potential reflection attack detected - peer echoed our DH key"));
            }
        }

        SodiumSecureMemoryHandle? rootKeyHandle = null;
        try
        {
            Result<PublicKeyBundle, EcliptixProtocolFailure> parseResult =
                Result<PublicKeyBundle, EcliptixProtocolFailure>.Try(
                    () =>
                    {
                        SecureByteStringInterop.SecureCopyWithCleanup(peerMessage.Payload, out byte[] payloadBytes);
                        try
                        {
                            return Helpers.ParseFromBytes<PublicKeyBundle>(payloadBytes);
                        }
                        finally
                        {
                            SodiumInterop.SecureWipe(payloadBytes);
                        }
                    },
                    ex => EcliptixProtocolFailure.Decode("Failed to parse peer public key bundle from protobuf.", ex));

            if (parseResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(parseResult.UnwrapErr());

            PublicKeyBundle protobufBundle = parseResult.Unwrap();

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> bundleResult =
                LocalPublicKeyBundle.FromProtobufExchange(protobufBundle);
            if (bundleResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());

            LocalPublicKeyBundle peerBundle = bundleResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> signatureResult = EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(
                peerBundle.IdentityEd25519, peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature);
            if (signatureResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());

            bool spkValid = signatureResult.Unwrap();
            if (!spkValid)
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Signed pre-key signature verification failed"));

            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> secretResult =
                _ecliptixSystemIdentityKeys.X3dhDeriveSharedSecret(peerBundle, Constants.X3dhInfo);
            if (secretResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(secretResult.UnwrapErr());

            SodiumSecureMemoryHandle derivedKeyHandle = secretResult.Unwrap();
            rootKeyHandle = derivedKeyHandle;

            Result<byte[], EcliptixProtocolFailure> rootKeyResult =
                ReadAndWipeSecureHandle(derivedKeyHandle, Constants.X25519KeySize);
            if (rootKeyResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(rootKeyResult.UnwrapErr());

            byte[] rootKeyBytes = rootKeyResult.Unwrap();

            SecureByteStringInterop.SecureCopyWithCleanup(peerMessage.InitialDhPublicKey, out byte[] dhKeyBytes);
            try
            {
                Result<Unit, EcliptixProtocolFailure> dhValidationResult =
                    DhValidator.ValidateX25519PublicKey(dhKeyBytes);
                if (dhValidationResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(dhValidationResult.UnwrapErr());

                Result<Unit, EcliptixProtocolFailure> finalizeResult =
                    _protocolConnection?.FinalizeChainAndDhKeys(rootKeyBytes, dhKeyBytes)
                    ?? Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic("Protocol connection not initialized"));
                if (finalizeResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(finalizeResult.UnwrapErr());

                Result<Unit, EcliptixProtocolFailure> setPeerResult = _protocolConnection?.SetPeerBundle(peerBundle)
                                                                      ?? Result<Unit, EcliptixProtocolFailure>.Err(
                                                                          EcliptixProtocolFailure.Generic(
                                                                              "Protocol connection not initialized"));
                if (setPeerResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(setPeerResult.UnwrapErr());
            }
            finally
            {
                SodiumInterop.SecureWipe(dhKeyBytes);
                SodiumInterop.SecureWipe(rootKeyBytes);
            }

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            rootKeyHandle?.Dispose();
        }
    }

    public Result<CipherPayload[], EcliptixProtocolFailure> ProduceOutboundMessageBatch(byte[][] plainPayloads)
    {
        if (plainPayloads.Length == 0)
            return Result<CipherPayload[], EcliptixProtocolFailure>.Ok([]);

        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<CipherPayload[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        List<CipherPayload> results = new(plainPayloads.Length);

        try
        {
            foreach (byte[] t in plainPayloads)
            {
                Result<CipherPayload, EcliptixProtocolFailure> singleResult =
                    ProduceSingleMessage(t, connection);
                if (singleResult.IsErr)
                    return Result<CipherPayload[], EcliptixProtocolFailure>.Err(singleResult.UnwrapErr());

                results.Add(singleResult.Unwrap());
            }

            return Result<CipherPayload[], EcliptixProtocolFailure>.Ok([.. results]);
        }
        catch (Exception ex)
        {
            return Result<CipherPayload[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Batch message production failed", ex));
        }
    }

    public Result<CipherPayload, EcliptixProtocolFailure> ProduceOutboundMessage(byte[] plainPayload)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<CipherPayload, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        return ProduceSingleMessage(plainPayload, connection);
    }

    private Result<CipherPayload, EcliptixProtocolFailure> ProduceSingleMessage(byte[] plainPayload,
        EcliptixProtocolConnection connection)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        _ratchetManager?.RecordMessage();

        bool isInitiator = connection.IsInitiator();
        Log.Information("Protocol message production started for {ConnectionRole} with payload size {PayloadSize}",
            isInitiator ? "Initiator" : "Responder", plainPayload.Length);
            
        Log.Debug("🔧 PRODUCE-MESSAGE: About to prepare next send message");

        EcliptixMessageKey? messageKeyClone = null;
        byte[]? nonce = null;
        byte[]? ad = null;
        byte[]? encrypted = null;
        byte[]? newSenderDhPublicKey = null;
        try
        {
            Result<(EcliptixMessageKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure> prepResult =
                connection.PrepareNextSendMessage();
            if (prepResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(prepResult.UnwrapErr());

            (EcliptixMessageKey MessageKey, bool IncludeDhKey) prep = prepResult.Unwrap();
            
            Log.Debug("🔧 PRODUCE-MESSAGE: PrepareNextSendMessage returned - Index={Index}, IncludeDhKey={IncludeDhKey}", 
                prep.MessageKey.Index, prep.IncludeDhKey);

            Result<byte[], EcliptixProtocolFailure> nonceResult = connection.GenerateNextNonce();
            if (nonceResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(nonceResult.UnwrapErr());

            nonce = nonceResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> dhKeyResult = GetOptionalSenderDhKey(prep.IncludeDhKey);
            if (dhKeyResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());

            newSenderDhPublicKey = dhKeyResult.Unwrap();
            
            if (prep.IncludeDhKey && newSenderDhPublicKey?.Length > 0)
            {
                Log.Information("[SERVER-RATCHET] Including new DH key in message at index {Index}, key length: {Length}", 
                    prep.MessageKey.Index, newSenderDhPublicKey.Length);
            }

            Result<EcliptixMessageKey, EcliptixProtocolFailure> cloneResult = CloneMessageKey(prep.MessageKey);
            if (cloneResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(cloneResult.UnwrapErr());

            messageKeyClone = cloneResult.Unwrap();

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool connectionIsInitiator = connection.IsInitiator();
            ad = connectionIsInitiator
                ? CreateAssociatedData(_ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, _ecliptixSystemIdentityKeys.IdentityX25519PublicKey);
            if (Log.IsEnabled(LogEventLevel.Debug))
                Log.Debug("Protocol encryption details - Role: {ConnectionRole}, MessageKeyIndex: {MessageKeyIndex}, " +
                          "SelfIdentityPrefix: {SelfIdentityPrefix}, PeerIdentityPrefix: {PeerIdentityPrefix}",
                    connectionIsInitiator ? "Initiator" : "Responder",
                    messageKeyClone!.Index,
                    Convert.ToHexString(_ecliptixSystemIdentityKeys.IdentityX25519PublicKey)[..16],
                    Convert.ToHexString(peerBundle.IdentityX25519)[..16]);

            Result<byte[], EcliptixProtocolFailure> encryptResult =
                Encrypt(messageKeyClone!, nonce, plainPayload, ad, connection);
            if (encryptResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(encryptResult.UnwrapErr());

            encrypted = encryptResult.Unwrap();

            CipherPayload payload = new()
            {
                RequestId = Helpers.GenerateRandomUInt32(true),
                Nonce = ByteString.CopyFrom(nonce),
                RatchetIndex = messageKeyClone!.Index,
                Cipher = ByteString.CopyFrom(encrypted),
                CreatedAt = GetProtoTimestamp(),
                DhPublicKey = newSenderDhPublicKey is { Length: > 0 }
                    ? ByteString.CopyFrom(newSenderDhPublicKey)
                    : ByteString.Empty
            };

            stopwatch.Stop();
            _metricsCollector.RecordOutboundMessage(stopwatch.Elapsed.TotalMilliseconds);
            _metricsCollector.RecordEncryption();

            return Result<CipherPayload, EcliptixProtocolFailure>.Ok(payload);
        }
        finally
        {
            messageKeyClone?.Dispose();
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
            if (ad != null) SodiumInterop.SecureWipe(ad);
            if (encrypted != null) SodiumInterop.SecureWipe(encrypted);
            if (newSenderDhPublicKey != null) SodiumInterop.SecureWipe(newSenderDhPublicKey);
        }
    }

    public Result<byte[][], EcliptixProtocolFailure> ProcessInboundMessageBatch(CipherPayload[] cipherPayloads)
    {
        if (cipherPayloads.Length == 0)
            return Result<byte[][], EcliptixProtocolFailure>.Ok([]);

        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        List<byte[]> results = new(cipherPayloads.Length);

        try
        {
            foreach (CipherPayload t in cipherPayloads)
            {
                Result<byte[], EcliptixProtocolFailure> singleResult =
                    ProcessSingleInboundMessage(t, connection);
                if (singleResult.IsErr)
                    return Result<byte[][], EcliptixProtocolFailure>.Err(singleResult.UnwrapErr());

                results.Add(singleResult.Unwrap());
            }

            return Result<byte[][], EcliptixProtocolFailure>.Ok([.. results]);
        }
        catch (Exception ex)
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Batch message processing failed", ex));
        }
    }

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundMessage(CipherPayload cipherPayloadProto)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        return ProcessSingleInboundMessage(cipherPayloadProto, connection);
    }

    private Result<byte[], EcliptixProtocolFailure> ProcessSingleInboundMessage(CipherPayload cipherPayloadProto,
        EcliptixProtocolConnection connection)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        _ratchetManager?.RecordMessage();

        EcliptixMessageKey? messageKeyClone = null;
        byte[]? receivedDhKey = null;
        byte[]? ad = null;
        try
        {
            if (cipherPayloadProto.DhPublicKey.Length > 0)
            {
                SecureByteStringInterop.SecureCopyWithCleanup(cipherPayloadProto.DhPublicKey, out receivedDhKey);
                
                Log.Information("[CLIENT-RATCHET] Received new DH key at message index {Index}, key length: {Length}",
                    cipherPayloadProto.RatchetIndex, cipherPayloadProto.DhPublicKey.Length);

                Result<Unit, EcliptixProtocolFailure> dhValidationResult =
                    DhValidator.ValidateX25519PublicKey(receivedDhKey!);
                if (dhValidationResult.IsErr)
                    return Result<byte[], EcliptixProtocolFailure>.Err(dhValidationResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformRatchetIfNeeded(receivedDhKey);
            if (ratchetResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());

            Result<Unit, EcliptixProtocolFailure> replayCheck = connection.CheckReplayProtection(
                [.. cipherPayloadProto.Nonce],
                cipherPayloadProto.RatchetIndex);
            if (replayCheck.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(replayCheck.UnwrapErr());

            Result<EcliptixMessageKey, EcliptixProtocolFailure> messageResult =
                connection.ProcessReceivedMessage(cipherPayloadProto.RatchetIndex);
            if (messageResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());

            EcliptixMessageKey clonedKey = messageResult.Unwrap();
            messageKeyClone = clonedKey;

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool isInitiator = connection.IsInitiator();
            ad = isInitiator
                ? CreateAssociatedData(_ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, _ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            if (Log.IsEnabled(LogEventLevel.Debug))
                Log.Debug("Protocol decryption details - Role: {ConnectionRole}, MessageKeyIndex: {MessageKeyIndex}, " +
                          "SelfIdentityPrefix: {SelfIdentityPrefix}, PeerIdentityPrefix: {PeerIdentityPrefix}",
                    isInitiator ? "Initiator" : "Responder",
                    messageKeyClone!.Index,
                    Convert.ToHexString(_ecliptixSystemIdentityKeys.IdentityX25519PublicKey)[..16],
                    Convert.ToHexString(peerBundle.IdentityX25519)[..16]);

            Result<byte[], EcliptixProtocolFailure> result = Decrypt(messageKeyClone!, cipherPayloadProto, ad,
                connection);

            stopwatch.Stop();
            if (result.IsOk)
            {
                _metricsCollector.RecordInboundMessage(stopwatch.Elapsed.TotalMilliseconds);
                _metricsCollector.RecordDecryption();
            }
            else
            {
                _metricsCollector.RecordError();
            }

            return result;
        }
        finally
        {
            messageKeyClone?.Dispose();
            if (receivedDhKey != null) SodiumInterop.SecureWipe(receivedDhKey);
            if (ad != null) SodiumInterop.SecureWipe(ad);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformRatchetIfNeeded(byte[]? receivedDhKey)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (receivedDhKey == null || connection == null)
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        Result<byte[]?, EcliptixProtocolFailure> currentKeyResult = connection.GetCurrentPeerDhPublicKey();
        if (currentKeyResult.IsErr)
            return Result<Unit, EcliptixProtocolFailure>.Err(currentKeyResult.UnwrapErr());

        byte[]? currentPeerDhKey = currentKeyResult.Unwrap();

        if (currentPeerDhKey != null && receivedDhKey.AsSpan().SequenceEqual(currentPeerDhKey))
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        Log.Information("Protocol using adaptive ratchet manager for intelligent ratcheting decisions");

        return PerformAtomicRatchet(receivedDhKey);
    }

    private Result<Unit, EcliptixProtocolFailure> PerformAtomicRatchet(byte[] receivedDhKey)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        connection.NotifyRatchetRotation();
        Log.Debug("Cleared replay protection before receiving ratchet rotation");

        Result<Unit, EcliptixProtocolFailure> ratchetResult = connection.PerformReceivingRatchet(receivedDhKey);
        if (ratchetResult.IsErr)
            return ratchetResult;

        connection.NotifyRatchetRotation();
        Log.Debug("Notified replay protection of receiving ratchet rotation");

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<byte[], EcliptixProtocolFailure> GetOptionalSenderDhKey(bool include)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        
        Log.Debug("🔧 GET-OPTIONAL-DH-KEY: Include={Include}, Connection={HasConnection}", 
            include, connection != null);
        
        if (!include || connection == null)
            return Result<byte[], EcliptixProtocolFailure>.Ok([]);

        Result<byte[]?, EcliptixProtocolFailure> keyResult = connection.GetCurrentSenderDhPublicKey();
        if (keyResult.IsErr)
            return Result<byte[], EcliptixProtocolFailure>.Err(keyResult.UnwrapErr());

        byte[]? key = keyResult.Unwrap();
        
        Log.Debug("🔧 GET-OPTIONAL-DH-KEY: Retrieved key length={Length}", key?.Length ?? 0);
        
        return Result<byte[], EcliptixProtocolFailure>.Ok(key ?? []);
    }

    private static Result<EcliptixMessageKey, EcliptixProtocolFailure> CloneMessageKey(EcliptixMessageKey key)
    {
        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AesKeySize);
        Span<byte> keySpan = keyMaterial.AsSpan();
        key.ReadKeyMaterial(keySpan);
        return EcliptixMessageKey.New(key.Index, keySpan);
    }

    private static byte[] CreateAssociatedData(byte[] id1, byte[] id2)
    {
        const int maxIdLength = 1024;
        if (id1.Length > maxIdLength || id2.Length > maxIdLength)
            throw new ArgumentException($"Identity keys too large (max {maxIdLength} bytes each)");

        if (id1.Length + id2.Length > int.MaxValue / 2)
            throw new ArgumentException("Combined identity keys would cause integer overflow");

        byte[] ad = new byte[id1.Length + id2.Length];
        Buffer.BlockCopy(id1, 0, ad, 0, id1.Length);
        Buffer.BlockCopy(id2, 0, ad, id1.Length, id2.Length);
        return ad;
    }

    private static Result<byte[], EcliptixProtocolFailure> Encrypt(EcliptixMessageKey key, byte[] nonce,
        byte[] plaintext, byte[] ad, EcliptixProtocolConnection? connection)
    {
        using IDisposable? timer = connection?.GetProfiler().StartOperation("AES-GCM-Encrypt");
        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AesKeySize);
        byte[]? ciphertext = null;
        try
        {
            Span<byte> keySpan = keyMaterial.AsSpan();
            Result<Unit, EcliptixProtocolFailure> readResult = key.ReadKeyMaterial(keySpan);
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());

            ciphertext = new byte[plaintext.Length];
            Span<byte> tag = stackalloc byte[Constants.AesGcmTagSize];

            using (AesGcm aesGcm = new(keySpan, Constants.AesGcmTagSize))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, ad);
            }

            byte[] result = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
            tag.CopyTo(result.AsSpan(ciphertext.Length));

            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
        }
        catch (Exception ex)
        {
            if (ciphertext != null) SodiumInterop.SecureWipe(ciphertext);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("AES-GCM encryption failed.", ex));
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> Decrypt(EcliptixMessageKey key, CipherPayload payload,
        byte[] ad, EcliptixProtocolConnection? connection)
    {
        using IDisposable? timer = connection?.GetProfiler().StartOperation("AES-GCM-Decrypt");
        ReadOnlySpan<byte> fullCipherSpan = payload.Cipher.Span;
        const int tagSize = Constants.AesGcmTagSize;
        int cipherLength = fullCipherSpan.Length - tagSize;

        if (cipherLength < 0)
            return Result<byte[], EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.BufferTooSmall(
                $"Received ciphertext length ({fullCipherSpan.Length}) is smaller than the GCM tag size ({tagSize})."));

        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AesKeySize);
        byte[]? ciphertext = null;
        byte[]? tag = null;
        byte[]? plaintext = null;
        byte[]? nonce = null;

        try
        {
            Span<byte> keySpan = keyMaterial.AsSpan();
            Result<Unit, EcliptixProtocolFailure> readResult = key.ReadKeyMaterial(keySpan);
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());

            ciphertext = fullCipherSpan[..cipherLength].ToArray();
            tag = fullCipherSpan[cipherLength..].ToArray();
            plaintext = new byte[cipherLength];
            nonce = [.. payload.Nonce];

            using (AesGcm aesGcm = new(keySpan, Constants.AesGcmTagSize))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, ad);
            }

            return Result<byte[], EcliptixProtocolFailure>.Ok(plaintext);
        }
        catch (CryptographicException cryptoEx)
        {
            if (ciphertext != null) SodiumInterop.SecureWipe(ciphertext);
            if (tag != null) SodiumInterop.SecureWipe(tag);
            if (plaintext != null) SodiumInterop.SecureWipe(plaintext);
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("AES-GCM decryption failed (authentication tag mismatch).", cryptoEx));
        }
        catch (Exception ex)
        {
            if (ciphertext != null) SodiumInterop.SecureWipe(ciphertext);
            if (tag != null) SodiumInterop.SecureWipe(tag);
            if (plaintext != null) SodiumInterop.SecureWipe(plaintext);
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Unexpected error during AES-GCM decryption.", ex));
        }
    }

    public static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> CreateFrom(EcliptixSystemIdentityKeys keys,
        EcliptixProtocolConnection connection)
    {
        Log.Information("🔧 PROTOCOL-SYSTEM-CREATE-DEFAULT: Creating protocol system with default config - DH every {Messages} messages", 
            RatchetConfig.Default.DhRatchetEveryNMessages);
            
        EcliptixProtocolSystem system = new(keys, RatchetConfig.Default) { _protocolConnection = connection };
        return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Ok(system);
    }

    public static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> CreateFrom(EcliptixSystemIdentityKeys keys,
        EcliptixProtocolConnection connection, RatchetConfig ratchetConfig)
    {
        EcliptixProtocolSystem system = new(keys, ratchetConfig) { _protocolConnection = connection };
        return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Ok(system);
    }

    private static Result<byte[], EcliptixProtocolFailure> ReadAndWipeSecureHandle(SodiumSecureMemoryHandle handle,
        int size)
    {
        byte[] buffer = new byte[size];
        Result<Unit, EcliptixProtocolFailure> readResult = handle.Read(buffer).MapSodiumFailure();
        if (readResult.IsErr)
            return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());

        byte[] copy = (byte[])buffer.Clone();
        SodiumInterop.SecureWipe(buffer);
        return Result<byte[], EcliptixProtocolFailure>.Ok(copy);
    }

    public EcliptixProtocolConnection? GetConnection() => GetConnectionSafe();

    public void ResetReplayProtection()
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        connection?.NotifyRatchetRotation();
    }


    public (LoadLevel Load, double MessageRate, uint RatchetInterval, TimeSpan MaxAge) GetLoadMetrics()
    {
        return _ratchetManager.GetLoadMetrics();
    }

    public LoadLevel CurrentLoadLevel => _ratchetManager.CurrentLoad;

    public RatchetConfig CurrentRatchetConfig => _ratchetManager.CurrentConfig;

    public void ForceLoadLevel(LoadLevel targetLoad)
    {
        _ratchetManager.ForceConfigUpdate(targetLoad);
    }

    public ProtocolMetrics GetProtocolMetrics()
    {
        _metricsCollector.UpdateExternalState(_ratchetManager.CurrentLoad, CircuitBreakerState.Closed);

        return _metricsCollector.GetCurrentMetrics();
    }

    public void LogPerformanceReport()
    {
        _metricsCollector.LogMetricsSummary();
    }

    public void ResetMetrics()
    {
        _metricsCollector.Reset();
    }

    public Result<AdaptiveRatchetState, EcliptixProtocolFailure> GetAdaptiveRatchetState()
    {
        if (_ratchetManager == null)
        {
            return Result<AdaptiveRatchetState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("AdaptiveRatchetManager not initialized"));
        }

        return _ratchetManager.ToProtoState();
    }

    private RatchetConfig GetConfigForExchangeType(PubKeyExchangeType exchangeType)
    {
        return exchangeType switch
        {
            PubKeyExchangeType.ServerStreaming => _ratchetManager.CurrentConfig,
            PubKeyExchangeType.DeviceToDevice => _ratchetManager.CurrentConfig,
            _ => RatchetConfig.Default
        };
    }
}