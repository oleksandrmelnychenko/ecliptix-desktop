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
    private EcliptixProtocolConnection? _protocolConnection;
    private IProtocolEventHandler? _eventHandler;

    public EcliptixSystemIdentityKeys GetIdentityKeys() => ecliptixSystemIdentityKeys;

    public void SetEventHandler(IProtocolEventHandler? handler)
    {
        lock (this)
        {
            _eventHandler = handler;
            _protocolConnection?.SetEventHandler(handler);
        }
    }

    public void Dispose()
    {
        _protocolConnection?.Dispose();
        GC.SuppressFinalize(this);
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
        _protocolConnection = session;
        _protocolConnection.SetEventHandler(_eventHandler);

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

    public Result<CipherPayload, EcliptixProtocolFailure> ProduceOutboundMessage(byte[] plainPayload)
    {
        if (_protocolConnection == null)
            return Result<CipherPayload, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        EcliptixMessageKey? messageKeyClone = null;
        byte[]? nonce = null;
        byte[]? ad = null;
        byte[]? encrypted = null;
        byte[]? newSenderDhPublicKey = null;
        try
        {
            Result<(EcliptixMessageKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure> prepResult =
                _protocolConnection.PrepareNextSendMessage();
            if (prepResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(prepResult.UnwrapErr());

            (EcliptixMessageKey MessageKey, bool IncludeDhKey) prep = prepResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> nonceResult = _protocolConnection.GenerateNextNonce();
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

            Result<PublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = _protocolConnection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519);

            Result<byte[], EcliptixProtocolFailure> encryptResult =
                Encrypt(messageKeyClone!, nonce, plainPayload, ad, _protocolConnection);
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

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundMessage(CipherPayload cipherPayloadProto)
    {
        if (_protocolConnection == null)
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Protocol connection not initialized"));

        EcliptixMessageKey? messageKeyClone = null;
        byte[]? receivedDhKey = null;
        byte[]? ad = null;
        try
        {
            Result<Unit, EcliptixProtocolFailure> replayCheck = _protocolConnection.CheckReplayProtection(
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
                _protocolConnection.ProcessReceivedMessage(cipherPayloadProto.RatchetIndex);
            if (messageResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());

            EcliptixMessageKey clonedKey = messageResult.Unwrap();
            messageKeyClone = clonedKey;

            Result<PublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = _protocolConnection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519);

            return Decrypt(messageKeyClone!, cipherPayloadProto, ad, _protocolConnection);
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
        if (receivedDhKey == null || _protocolConnection == null)
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        Result<byte[]?, EcliptixProtocolFailure> currentKeyResult = _protocolConnection.GetCurrentPeerDhPublicKey();
        if (currentKeyResult.IsErr)
            return Result<Unit, EcliptixProtocolFailure>.Err(currentKeyResult.UnwrapErr());

        byte[]? currentPeerDhKey = currentKeyResult.Unwrap();

        if (currentPeerDhKey != null && receivedDhKey.AsSpan().SequenceEqual(currentPeerDhKey))
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        return _protocolConnection.PerformReceivingRatchet(receivedDhKey);
    }

    private Result<byte[], EcliptixProtocolFailure> GetOptionalSenderDhKey(bool include)
    {
        if (!include || _protocolConnection == null)
            return Result<byte[], EcliptixProtocolFailure>.Ok([]);

        Result<byte[]?, EcliptixProtocolFailure> keyResult = _protocolConnection.GetCurrentSenderDhPublicKey();
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

    public EcliptixProtocolConnection? GetConnection() => _protocolConnection;
}