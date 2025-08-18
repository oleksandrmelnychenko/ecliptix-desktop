using System.Security.Cryptography;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.ProtocolState;
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
        _eventHandler = handler;
        _protocolConnection?.SetEventHandler(handler);
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

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> sessionResult = EcliptixProtocolConnection.Create(connectId, true);
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
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.PrepareLocal("DH public key is null"));

        PubKeyExchange pubKeyExchange = new()
        {
            State = PubKeyExchangeState.Init,
            OfType = exchangeType,
            Payload = bundle.ToProtobufExchange().ToByteString(),
            InitialDhPublicKey = ByteString.CopyFrom(dhPublicKey)
        };

        return Result<PubKeyExchange, EcliptixProtocolFailure>.Ok(pubKeyExchange);
    }

    public void CompleteDataCenterPubKeyExchange(PubKeyExchange peerMessage)
    {
        SodiumSecureMemoryHandle? rootKeyHandle = null;
        try
        {
            Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure> parseResult = Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure>.Try(
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
                return;

            Protobuf.PubKeyExchange.PublicKeyBundle protobufBundle = parseResult.Unwrap();

            Result<PublicKeyBundle, EcliptixProtocolFailure> bundleResult = PublicKeyBundle.FromProtobufExchange(protobufBundle);
            if (bundleResult.IsErr)
                return;

            PublicKeyBundle peerBundle = bundleResult.Unwrap();

            Result<bool, EcliptixProtocolFailure> signatureResult = EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(
                peerBundle.IdentityEd25519, peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature);
            if (signatureResult.IsErr)
                return;

            bool spkValid = signatureResult.Unwrap();
            if (!spkValid)
                return;

            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> secretResult = ecliptixSystemIdentityKeys.X3dhDeriveSharedSecret(peerBundle, Constants.X3dhInfo);
            if (secretResult.IsErr)
                return;

            SodiumSecureMemoryHandle derivedKeyHandle = secretResult.Unwrap();
            rootKeyHandle = derivedKeyHandle;

            Result<byte[], EcliptixProtocolFailure> rootKeyResult = ReadAndWipeSecureHandle(derivedKeyHandle, Constants.X25519KeySize);
            if (rootKeyResult.IsErr)
                return;

            byte[] rootKeyBytes = rootKeyResult.Unwrap();

            SecureByteStringInterop.SecureCopyWithCleanup(peerMessage.InitialDhPublicKey, out byte[] dhKeyBytes);
            try
            {
                Result<Unit, EcliptixProtocolFailure> finalizeResult = _protocolConnection!.FinalizeChainAndDhKeys(rootKeyBytes, dhKeyBytes);
                if (finalizeResult.IsErr)
                    return;

                Result<Unit, EcliptixProtocolFailure> setPeerResult = _protocolConnection!.SetPeerBundle(peerBundle);
                if (setPeerResult.IsErr)
                    return;
            }
            finally
            {
                SodiumInterop.SecureWipe(dhKeyBytes);
            }
        }
        finally
        {
            rootKeyHandle?.Dispose();
        }
    }

    public Result<CipherPayload, EcliptixProtocolFailure> ProduceOutboundMessage(byte[] plainPayload)
    {
        EcliptixMessageKey? messageKeyClone = null;
        try
        {
            Result<(EcliptixMessageKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure> prepResult = _protocolConnection!.PrepareNextSendMessage();
            if (prepResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(prepResult.UnwrapErr());

            (EcliptixMessageKey MessageKey, bool IncludeDhKey) prep = prepResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> nonceResult = _protocolConnection.GenerateNextNonce();
            if (nonceResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(nonceResult.UnwrapErr());

            byte[] nonce = nonceResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> dhKeyResult = GetOptionalSenderDhKey(prep.IncludeDhKey);
            if (dhKeyResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());

            byte[] newSenderDhPublicKey = dhKeyResult.Unwrap();

            Result<EcliptixMessageKey, EcliptixProtocolFailure> cloneResult = CloneMessageKey(prep.MessageKey);
            if (cloneResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(cloneResult.UnwrapErr());

            messageKeyClone = cloneResult.Unwrap();

            Result<PublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = _protocolConnection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            byte[] ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519);

            Result<byte[], EcliptixProtocolFailure> encryptResult = Encrypt(messageKeyClone!, nonce, plainPayload, ad);
            if (encryptResult.IsErr)
                return Result<CipherPayload, EcliptixProtocolFailure>.Err(encryptResult.UnwrapErr());

            byte[] encrypted = encryptResult.Unwrap();

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
        }
    }

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundMessage(CipherPayload cipherPayloadProto)
    {
        EcliptixMessageKey? messageKeyClone = null;
        try
        {
            byte[]? receivedDhKey = null;
            if (cipherPayloadProto.DhPublicKey.Length > 0)
            {
                SecureByteStringInterop.SecureCopyWithCleanup(cipherPayloadProto.DhPublicKey, out receivedDhKey);
            }

            Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformRatchetIfNeeded(receivedDhKey);
            if (ratchetResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());

            Result<EcliptixMessageKey, EcliptixProtocolFailure> messageResult = _protocolConnection!.ProcessReceivedMessage(cipherPayloadProto.RatchetIndex);
            if (messageResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());

            EcliptixMessageKey clonedKey = messageResult.Unwrap();
            messageKeyClone = clonedKey;

            Result<PublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = _protocolConnection!.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            PublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            byte[] ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519);

            return Decrypt(messageKeyClone!, cipherPayloadProto, ad);
        }
        finally
        {
            messageKeyClone?.Dispose();
        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformRatchetIfNeeded(byte[]? receivedDhKey)
    {
        if (receivedDhKey == null)
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        Result<byte[]?, EcliptixProtocolFailure> currentKeyResult = _protocolConnection!.GetCurrentPeerDhPublicKey();
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
        if (!include)
            return Result<byte[], EcliptixProtocolFailure>.Ok([]);

        Result<byte[]?, EcliptixProtocolFailure> keyResult = _protocolConnection!.GetCurrentSenderDhPublicKey();
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
        byte[] ad = new byte[id1.Length + id2.Length];
        Buffer.BlockCopy(id1, 0, ad, 0, id1.Length);
        Buffer.BlockCopy(id2, 0, ad, id1.Length, id2.Length);
        return ad;
    }

    private static Result<byte[], EcliptixProtocolFailure> Encrypt(EcliptixMessageKey key, byte[] nonce,
        byte[] plaintext, byte[] ad)
    {
        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AesKeySize);
        try
        {
            Span<byte> keySpan = keyMaterial.AsSpan();
            Result<Unit, EcliptixProtocolFailure> readResult = key.ReadKeyMaterial(keySpan);
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());


            byte[] ciphertext = new byte[plaintext.Length];
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
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("AES-GCM encryption failed.", ex));
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> Decrypt(EcliptixMessageKey key, CipherPayload payload,
        byte[] ad)
    {
        ReadOnlySpan<byte> fullCipherSpan = payload.Cipher.Span;
        const int tagSize = Constants.AesGcmTagSize;
        int cipherLength = fullCipherSpan.Length - tagSize;

        if (cipherLength < 0)
            return Result<byte[], EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.BufferTooSmall(
                $"Received ciphertext length ({fullCipherSpan.Length}) is smaller than the GCM tag size ({tagSize})."));

        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AesKeySize);

        try
        {
            Span<byte> keySpan = keyMaterial.AsSpan();
            Result<Unit, EcliptixProtocolFailure> readResult = key.ReadKeyMaterial(keySpan);
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());

            byte[] ciphertext = fullCipherSpan[..cipherLength].ToArray();
            byte[] tag = fullCipherSpan[cipherLength..].ToArray();
            byte[] plaintext = new byte[cipherLength];

            using (AesGcm aesGcm = new(keySpan, Constants.AesGcmTagSize))
            {
                aesGcm.Decrypt(payload.Nonce.ToArray(), ciphertext, tag, plaintext, ad);
            }

            return Result<byte[], EcliptixProtocolFailure>.Ok(plaintext);
        }
        catch (CryptographicException cryptoEx)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("AES-GCM decryption failed (authentication tag mismatch).", cryptoEx));
        }
        catch (Exception ex)
        {
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

    public EcliptixProtocolConnection GetConnection()
    {
        if (_protocolConnection == null) throw new InvalidOperationException("Connection has not been established yet.");
        return _protocolConnection;
    }
}