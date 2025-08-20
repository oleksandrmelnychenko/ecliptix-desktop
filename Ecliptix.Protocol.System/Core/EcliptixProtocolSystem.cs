using System.Diagnostics;
using System.Security.Cryptography;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Ecliptix.Protocol.System.Core;

public class EcliptixProtocolSystem(EcliptixSystemIdentityKeys ecliptixSystemIdentityKeys) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly CircuitBreaker _circuitBreaker = new(
        failureThreshold: 10,
        timeout: TimeSpan.FromSeconds(60),
        successThresholdPercentage: 0.7);
    private readonly AdaptiveRatchetManager _ratchetManager = new(RatchetConfig.Default);
    private readonly ProtocolMetricsCollector _metricsCollector = new(TimeSpan.FromSeconds(30));
    private EcliptixProtocolConnection? _protocolConnection;
    private IProtocolEventHandler? _eventHandler;

    public EcliptixSystemIdentityKeys GetIdentityKeys() => ecliptixSystemIdentityKeys;

    public void SetEventHandler(IProtocolEventHandler? handler)
    {
        EcliptixProtocolConnection? connectionToUpdate;

        lock (_lock)
        {
            _eventHandler = handler;
            connectionToUpdate = _protocolConnection;
        }

        // Call external method outside of lock to prevent deadlock
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

        // Dispose outside of lock to prevent deadlock
        connectionToDispose?.Dispose();
        _circuitBreaker.Dispose();
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
        ecliptixSystemIdentityKeys.GenerateEphemeralKeyPair();

        Result<PublicKeyBundle, EcliptixProtocolFailure> bundleResult = ecliptixSystemIdentityKeys.CreatePublicBundle();
        if (bundleResult.IsErr)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());

        PublicKeyBundle bundle = bundleResult.Unwrap();

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> sessionResult =
            EcliptixProtocolConnection.Create(connectId, true, RatchetConfig.Default);
        if (sessionResult.IsErr)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(sessionResult.UnwrapErr());

        EcliptixProtocolConnection session = sessionResult.Unwrap();

        IProtocolEventHandler? currentHandler;
        lock (_lock)
        {
            _protocolConnection = session;
            currentHandler = _eventHandler;
        }

        // Set handler outside lock to prevent deadlock
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
            Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure> parseResult =
                Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure>.Try(
                    () =>
                    {
                        SecureByteStringInterop.SecureCopyWithCleanup(peerMessage.Payload, out byte[] payloadBytes);
                        try
                        {
                            return Helpers.ParseFromBytes<Protobuf.PubKeyExchange.PublicKeyBundle>(payloadBytes);
                        }
                        finally
                        {
                            SodiumInterop.SecureWipe(payloadBytes);
                        }
                    },
                    ex => EcliptixProtocolFailure.Decode("Failed to parse peer public key bundle from protobuf.", ex));

            if (parseResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(parseResult.UnwrapErr());

            Protobuf.PubKeyExchange.PublicKeyBundle protobufBundle = parseResult.Unwrap();

            Result<PublicKeyBundle, EcliptixProtocolFailure> bundleResult =
                PublicKeyBundle.FromProtobufExchange(protobufBundle);
            if (bundleResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = bundleResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> signatureResult = EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(
                peerBundle.IdentityEd25519, peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature);
            if (signatureResult.IsErr)
                return Result<Unit, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());

            bool spkValid = signatureResult.Unwrap();
            if (!spkValid)
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Signed pre-key signature verification failed"));

            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> secretResult =
                ecliptixSystemIdentityKeys.X3dhDeriveSharedSecret(peerBundle, Constants.X3dhInfo);
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
        return _circuitBreaker.Execute(() =>
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
                // Batch process all messages to reduce lock overhead
                for (int i = 0; i < plainPayloads.Length; i++)
                {
                    Result<CipherPayload, EcliptixProtocolFailure> singleResult =
                        ProduceSingleMessage(plainPayloads[i], connection);
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
        });
    }

    public Result<CipherPayload, EcliptixProtocolFailure> ProduceOutboundMessage(byte[] plainPayload)
    {
        return _circuitBreaker.Execute(() =>
        {
            EcliptixProtocolConnection? connection = GetConnectionSafe();
            if (connection == null)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

            return ProduceSingleMessage(plainPayload, connection);
        });
    }

    private Result<CipherPayload, EcliptixProtocolFailure> ProduceSingleMessage(byte[] plainPayload, EcliptixProtocolConnection connection)
    {
        var stopwatch = Stopwatch.StartNew();

        // Record message for adaptive load management
        _ratchetManager.RecordMessage();

        // CRITICAL DEBUG: Check client IsInitiator status
        bool debugIsInitiator = connection.IsInitiator();
        Console.WriteLine($"[CLIENT-DEBUG] ProduceSingleMessage - IsInitiator: {debugIsInitiator}");

        // Also write to file so we can definitely find it
        string debugPath = "/tmp/ecliptix_client_debug.log";
        string debugMsg = $"[{DateTime.UtcNow:HH:mm:ss.fff}] CLIENT IsInitiator: {debugIsInitiator}\n";
        File.AppendAllText(debugPath, debugMsg);

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

            Result<byte[], EcliptixProtocolFailure> nonceResult = connection.GenerateNextNonce();
            if (nonceResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(nonceResult.UnwrapErr());

            nonce = nonceResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> dhKeyResult = GetOptionalSenderDhKey(prep.IncludeDhKey);
            if (dhKeyResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());

            newSenderDhPublicKey = dhKeyResult.Unwrap();

            Result<EcliptixMessageKey, EcliptixProtocolFailure> cloneResult = CloneMessageKey(prep.MessageKey);
            if (cloneResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(cloneResult.UnwrapErr());

            messageKeyClone = cloneResult.Unwrap();

            Result<PublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool isInitiator = connection.IsInitiator();
            ad = isInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            Console.WriteLine($"[CLIENT-ENCRYPT] IsInitiator: {isInitiator}");
            Console.WriteLine($"[CLIENT-ENCRYPT] Self Identity: {Convert.ToHexString(ecliptixSystemIdentityKeys.IdentityX25519PublicKey)[..16]}...");
            Console.WriteLine($"[CLIENT-ENCRYPT] Peer Identity: {Convert.ToHexString(peerBundle.IdentityX25519)[..16]}...");
            Console.WriteLine($"[CLIENT-ENCRYPT] AD (init?self||peer:peer||self): {Convert.ToHexString(ad)[..32]}...");
            Console.WriteLine($"[CLIENT-ENCRYPT] Message key index: {messageKeyClone!.Index}");
            Console.WriteLine($"[CLIENT-ENCRYPT] Nonce: {Convert.ToHexString(nonce)[..24]}...");

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
        return _circuitBreaker.Execute(() =>
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
                // Batch process all messages to reduce lock overhead
                for (int i = 0; i < cipherPayloads.Length; i++)
                {
                    Result<byte[], EcliptixProtocolFailure> singleResult =
                        ProcessSingleInboundMessage(cipherPayloads[i], connection);
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
        });
    }

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundMessage(CipherPayload cipherPayloadProto)
    {
        return _circuitBreaker.Execute(() =>
        {
            EcliptixProtocolConnection? connection = GetConnectionSafe();
            if (connection == null)
                return Result<byte[], EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

            return ProcessSingleInboundMessage(cipherPayloadProto, connection);
        });
    }

    private Result<byte[], EcliptixProtocolFailure> ProcessSingleInboundMessage(CipherPayload cipherPayloadProto, EcliptixProtocolConnection connection)
    {
        var stopwatch = Stopwatch.StartNew();

        // Record message for adaptive load management
        _ratchetManager.RecordMessage();

        EcliptixMessageKey? messageKeyClone = null;
        byte[]? receivedDhKey = null;
        byte[]? ad = null;
        try
        {
            Result<Unit, EcliptixProtocolFailure> replayCheck = connection.CheckReplayProtection(
                [.. cipherPayloadProto.Nonce],
                cipherPayloadProto.RatchetIndex);
            if (replayCheck.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(replayCheck.UnwrapErr());

            if (cipherPayloadProto.DhPublicKey.Length > 0)
            {
                SecureByteStringInterop.SecureCopyWithCleanup(cipherPayloadProto.DhPublicKey, out receivedDhKey);

                Result<Unit, EcliptixProtocolFailure> dhValidationResult =
                    DhValidator.ValidateX25519PublicKey(receivedDhKey!);
                if (dhValidationResult.IsErr)
                    return Result<byte[], EcliptixProtocolFailure>.Err(dhValidationResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformRatchetIfNeeded(receivedDhKey);
            if (ratchetResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());

            Result<EcliptixMessageKey, EcliptixProtocolFailure> messageResult =
                connection.ProcessReceivedMessage(cipherPayloadProto.RatchetIndex);
            if (messageResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());

            EcliptixMessageKey clonedKey = messageResult.Unwrap();
            messageKeyClone = clonedKey;

            Result<PublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool isInitiator = connection.IsInitiator();
            ad = isInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            Console.WriteLine($"[CLIENT-DECRYPT] IsInitiator: {isInitiator}");
            Console.WriteLine($"[CLIENT-DECRYPT] Self Identity: {Convert.ToHexString(ecliptixSystemIdentityKeys.IdentityX25519PublicKey)[..16]}...");
            Console.WriteLine($"[CLIENT-DECRYPT] Peer Identity: {Convert.ToHexString(peerBundle.IdentityX25519)[..16]}...");
            Console.WriteLine($"[CLIENT-DECRYPT] AD (init?self||peer:peer||self): {Convert.ToHexString(ad)[..32]}...");
            Console.WriteLine($"[CLIENT-DECRYPT] Message key index: {messageKeyClone!.Index}");
            Console.WriteLine($"[CLIENT-DECRYPT] Nonce: {Convert.ToHexString(cipherPayloadProto.Nonce.Span)[..24]}...");

            Result<byte[], EcliptixProtocolFailure> result = Decrypt(messageKeyClone!, cipherPayloadProto, ad, connection);

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

        // Use AdaptiveRatchetManager to make intelligent ratcheting decisions
        // This integrates load-aware ratcheting decisions into the client
        Console.WriteLine("[CLIENT] Using adaptive ratchet manager for intelligent ratcheting decisions");

        // Atomic ratchet operation to prevent race conditions
        return PerformAtomicRatchet(receivedDhKey);
    }

    private Result<Unit, EcliptixProtocolFailure> PerformAtomicRatchet(byte[] receivedDhKey)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        // Clear replay protection before ratchet to handle any pending messages
        connection.NotifyRatchetRotation();
        Console.WriteLine("[CLIENT] Cleared replay protection before receiving ratchet rotation");

        // Perform the actual ratchet operation
        Result<Unit, EcliptixProtocolFailure> ratchetResult = connection.PerformReceivingRatchet(receivedDhKey);
        if (ratchetResult.IsErr)
            return ratchetResult;

        // Notify replay protection after successful ratchet
        connection.NotifyRatchetRotation();
        Console.WriteLine("[CLIENT] Notified replay protection of receiving ratchet rotation");

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<byte[], EcliptixProtocolFailure> GetOptionalSenderDhKey(bool include)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (!include || connection == null)
            return Result<byte[], EcliptixProtocolFailure>.Ok([]);

        Result<byte[]?, EcliptixProtocolFailure> keyResult = connection.GetCurrentSenderDhPublicKey();
        if (keyResult.IsErr)
            return Result<byte[], EcliptixProtocolFailure>.Err(keyResult.UnwrapErr());

        byte[]? key = keyResult.Unwrap();
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
        EcliptixProtocolSystem system = new(keys) { _protocolConnection = connection };
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

    public (CircuitBreakerState State, int FailureCount, int SuccessCount, DateTime LastFailure) GetCircuitBreakerStatus()
    {
        return _circuitBreaker.GetStatus();
    }

    public void ResetCircuitBreaker()
    {
        _circuitBreaker.Reset();
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
        // Update external state in metrics collector
        var circuitStatus = _circuitBreaker.GetStatus();
        _metricsCollector.UpdateExternalState(_ratchetManager.CurrentLoad, circuitStatus.State);

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
}