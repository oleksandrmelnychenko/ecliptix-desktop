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

namespace Ecliptix.Protocol.System.Core;

public class EcliptixProtocolSystem(EcliptixSystemIdentityKeys ecliptixSystemIdentityKeys)
    : IDisposable
{
    private readonly Lock _lock = new();

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

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> bundleResult = ecliptixSystemIdentityKeys.CreatePublicBundle();
        if (bundleResult.IsErr)
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());

        LocalPublicKeyBundle bundle = bundleResult.Unwrap();

        RatchetConfig configToUse = GetConfigForExchangeType(exchangeType);

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> sessionResult =
            EcliptixProtocolConnection.Create(connectId, true, configToUse, exchangeType);
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
                EcliptixProtocolFailure.PrepareLocal(ProtocolSystemConstants.ProtocolSystem.DhPublicKeyNullMessage));

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
                                                                      EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.NoConnectionMessage));
        if (ourDhKeyResult.IsOk)
        {
            byte[]? ourDhKey = ourDhKeyResult.Unwrap();
            if (ourDhKey != null && peerMessage.InitialDhPublicKey.ToArray().AsSpan().SequenceEqual(ourDhKey))
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ReflectionAttackMessage));
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
                    ex => EcliptixProtocolFailure.Decode(ProtocolSystemConstants.ProtocolSystem.ParseProtobufFailedMessage, ex));

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
                    EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.SignedPreKeyFailedMessage));

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
                        EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));
                if (finalizeResult.IsErr)
                    return Result<Unit, EcliptixProtocolFailure>.Err(finalizeResult.UnwrapErr());

                Result<Unit, EcliptixProtocolFailure> setPeerResult = _protocolConnection?.SetPeerBundle(peerBundle)
                                                                      ?? Result<Unit, EcliptixProtocolFailure>.Err(
                                                                          EcliptixProtocolFailure.Generic(
                                                                              ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));
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

    public Result<SecureEnvelope, EcliptixProtocolFailure> ProduceOutboundMessage(byte[] plainPayload)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));

        return ProduceSingleMessage(plainPayload, connection);
    }

    private Result<SecureEnvelope, EcliptixProtocolFailure> ProduceSingleMessage(byte[] plainPayload,
        EcliptixProtocolConnection connection)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        byte[]? nonce = null;
        byte[]? ad = null;
        byte[]? encrypted = null;
        byte[]? newSenderDhPublicKey = null;
        byte[]? metadataKey = null;
        byte[]? metadataNonce = null;
        byte[]? encryptedMetadata = null;
        try
        {
            Result<(RatchetChainKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure> prepResult =
                connection.PrepareNextSendMessage();
            if (prepResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(prepResult.UnwrapErr());

            (RatchetChainKey MessageKey, bool IncludeDhKey) prep = prepResult.Unwrap();


            Result<byte[], EcliptixProtocolFailure> nonceResult = connection.GenerateNextNonce();
            if (nonceResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(nonceResult.UnwrapErr());

            nonce = nonceResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> dhKeyResult = GetOptionalSenderDhKey(prep.IncludeDhKey);
            if (dhKeyResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());

            newSenderDhPublicKey = dhKeyResult.Unwrap();

            if (prep.IncludeDhKey && newSenderDhPublicKey?.Length > ProtocolSystemConstants.ProtocolSystem.EmptyArrayLength)
            {
            }

            RatchetChainKey? messageKey = prep.MessageKey;

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool connectionIsInitiator = connection.IsInitiator();
            ad = connectionIsInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            Result<byte[], EcliptixProtocolFailure> encryptResult =
                Encrypt(messageKey!, nonce, plainPayload, ad, connection);
            if (encryptResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(encryptResult.UnwrapErr());

            encrypted = encryptResult.Unwrap();

            uint requestId = Helpers.GenerateRandomUInt32(true);

            EnvelopeMetadata metadata = EnvelopeBuilder.CreateEnvelopeMetadata(
                requestId,
                ByteString.CopyFrom(nonce),
                messageKey!.Index);

            Result<byte[], EcliptixProtocolFailure> metadataKeyResult = connection.GetMetadataEncryptionKey();
            if (metadataKeyResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(metadataKeyResult.UnwrapErr());
            metadataKey = metadataKeyResult.Unwrap();

            metadataNonce = new byte[Constants.AesGcmNonceSize];
            RandomNumberGenerator.Fill(metadataNonce);

            Result<byte[], EcliptixProtocolFailure> encryptMetadataResult =
                EnvelopeBuilder.EncryptMetadata(metadata, metadataKey, metadataNonce, ad);
            if (encryptMetadataResult.IsErr)
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(encryptMetadataResult.UnwrapErr());
            encryptedMetadata = encryptMetadataResult.Unwrap();

            SecureEnvelope envelope = new()
            {
                MetaData = ByteString.CopyFrom(encryptedMetadata),
                EncryptedPayload = ByteString.CopyFrom(encrypted),
                HeaderNonce = ByteString.CopyFrom(metadataNonce),
                Timestamp = GetProtoTimestamp(),
                ResultCode = ByteString.CopyFrom(BitConverter.GetBytes((int)EnvelopeResultCode.Success)),
                DhPublicKey = newSenderDhPublicKey is { Length: > ProtocolSystemConstants.ProtocolSystem.EmptyArrayLength }
                    ? ByteString.CopyFrom(newSenderDhPublicKey)
                    : ByteString.Empty
            };

            stopwatch.Stop();

            return Result<SecureEnvelope, EcliptixProtocolFailure>.Ok(envelope);
        }
        finally
        {
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
            if (ad != null) SodiumInterop.SecureWipe(ad);
            if (encrypted != null) SodiumInterop.SecureWipe(encrypted);
            if (newSenderDhPublicKey != null) SodiumInterop.SecureWipe(newSenderDhPublicKey);
            if (metadataKey != null) SodiumInterop.SecureWipe(metadataKey);
            if (encryptedMetadata != null) Array.Clear(encryptedMetadata);
        }
    }

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundEnvelope(SecureEnvelope secureEnvelope)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));

        return ProcessInboundEnvelopeInternal(secureEnvelope, connection);
    }

    private Result<byte[], EcliptixProtocolFailure> ProcessInboundEnvelopeInternal(SecureEnvelope secureEnvelope,
        EcliptixProtocolConnection connection)
    {
        byte[]? dhPublicKey = null;
        byte[]? headerNonce = null;
        byte[]? metadataKey = null;
        byte[]? ad = null;
        byte[]? encryptedMetadata = null;
        try
        {
            if (secureEnvelope.DhPublicKey != null && !secureEnvelope.DhPublicKey.IsEmpty)
            {
                dhPublicKey = secureEnvelope.DhPublicKey.ToByteArray();
            }

            if (dhPublicKey != null)
            {
                Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformRatchetIfNeeded(dhPublicKey);
                if (ratchetResult.IsErr)
                    return Result<byte[], EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());
            }

            if (secureEnvelope.HeaderNonce == null || secureEnvelope.HeaderNonce.IsEmpty ||
                secureEnvelope.HeaderNonce.Length != Constants.AesGcmNonceSize)
                return Result<byte[], EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Decode("Invalid or missing header nonce for metadata decryption"));

            headerNonce = secureEnvelope.HeaderNonce.ToByteArray();

            Result<byte[], EcliptixProtocolFailure> metadataKeyResult = connection.GetMetadataEncryptionKey();
            if (metadataKeyResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(metadataKeyResult.UnwrapErr());
            metadataKey = metadataKeyResult.Unwrap();

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());
            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool connectionIsInitiator = connection.IsInitiator();
            ad = connectionIsInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            encryptedMetadata = secureEnvelope.MetaData.ToByteArray();
            Result<EnvelopeMetadata, EcliptixProtocolFailure> metadataResult =
                EnvelopeBuilder.DecryptMetadata(encryptedMetadata, metadataKey, headerNonce, ad);
            if (metadataResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(metadataResult.UnwrapErr());

            EnvelopeMetadata metadata = metadataResult.Unwrap();
            byte[] encryptedPayload = secureEnvelope.EncryptedPayload.ToByteArray();

            return ProcessInboundEnvelopeFromMaterials(metadata, encryptedPayload, connection);
        }
        finally
        {
            if (dhPublicKey != null) SodiumInterop.SecureWipe(dhPublicKey);
            if (headerNonce != null) SodiumInterop.SecureWipe(headerNonce);
            if (metadataKey != null) SodiumInterop.SecureWipe(metadataKey);
            if (ad != null) SodiumInterop.SecureWipe(ad);
            if (encryptedMetadata != null) Array.Clear(encryptedMetadata);
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

        return PerformAtomicRatchet(receivedDhKey);
    }

    private Result<Unit, EcliptixProtocolFailure> PerformAtomicRatchet(byte[] receivedDhKey)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));

        connection.NotifyRatchetRotation();

        Result<Unit, EcliptixProtocolFailure> ratchetResult = connection.PerformReceivingRatchet(receivedDhKey);
        if (ratchetResult.IsErr)
            return ratchetResult;

        connection.NotifyRatchetRotation();
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


    private static byte[] CreateAssociatedData(byte[] id1, byte[] id2)
    {
        int maxIdLength = ProtocolSystemConstants.ProtocolSystem.MaxIdentityKeyLength;
        if (id1.Length > maxIdLength || id2.Length > maxIdLength)
            throw new ArgumentException(string.Format(ProtocolSystemConstants.ProtocolSystem.IdentityKeysTooLargeMessage, maxIdLength));

        if (id1.Length + id2.Length > int.MaxValue / ProtocolSystemConstants.ProtocolSystem.IntegerOverflowDivisor)
            throw new ArgumentException(ProtocolSystemConstants.ProtocolSystem.IntegerOverflowMessage);

        byte[] ad = new byte[id1.Length + id2.Length];
        Buffer.BlockCopy(id1, ProtocolSystemConstants.ProtocolSystem.BufferCopyStartOffset, ad, ProtocolSystemConstants.ProtocolSystem.BufferCopyStartOffset, id1.Length);
        Buffer.BlockCopy(id2, ProtocolSystemConstants.ProtocolSystem.BufferCopyStartOffset, ad, id1.Length, id2.Length);
        return ad;
    }

    private static Result<byte[], EcliptixProtocolFailure> Encrypt(RatchetChainKey key, byte[] nonce,
        byte[] plaintext, byte[] ad, EcliptixProtocolConnection? connection)
    {
        using IDisposable? timer = connection?.GetProfiler().StartOperation(ProtocolSystemConstants.ProtocolSystem.AesGcmEncryptOperationName);
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
            Buffer.BlockCopy(ciphertext, ProtocolSystemConstants.ProtocolSystem.BufferCopyStartOffset, result, ProtocolSystemConstants.ProtocolSystem.BufferCopyStartOffset, ciphertext.Length);
            tag.CopyTo(result.AsSpan(ciphertext.Length));

            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
        }
        catch (Exception ex)
        {
            if (ciphertext != null) SodiumInterop.SecureWipe(ciphertext);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.AesGcmEncryptionFailedMessage, ex));
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> Decrypt(RatchetChainKey key, SecureEnvelope envelope,
        byte[] ad, EcliptixProtocolConnection? connection)
    {
        Result<EnvelopeMetadata, EcliptixProtocolFailure> metadataResult =
            EnvelopeBuilder.ParseEnvelopeMetadata(envelope.MetaData);
        if (metadataResult.IsErr)
            return Result<byte[], EcliptixProtocolFailure>.Err(metadataResult.UnwrapErr());

        EnvelopeMetadata metadata = metadataResult.Unwrap();
        byte[] encryptedPayload = envelope.EncryptedPayload.ToByteArray();
        return DecryptFromComponents(key, metadata, encryptedPayload, ad, connection);
    }

    private static Result<byte[], EcliptixProtocolFailure> DecryptLegacy(RatchetChainKey key, EnvelopeMetadata metadata, byte[] encryptedPayload,
        byte[] ad, EcliptixProtocolConnection? connection)
    {
        using IDisposable? timer = connection?.GetProfiler().StartOperation(ProtocolSystemConstants.ProtocolSystem.AesGcmDecryptOperationName);
        ReadOnlySpan<byte> fullCipherSpan = encryptedPayload.AsSpan();
        const int tagSize = Constants.AesGcmTagSize;
        int cipherLength = fullCipherSpan.Length - tagSize;

        if (cipherLength < ProtocolSystemConstants.ProtocolSystem.CipherLengthMinimumThreshold)
            return Result<byte[], EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.BufferTooSmall(
                string.Format(ProtocolSystemConstants.ProtocolSystem.CiphertextTooSmallMessage, fullCipherSpan.Length, tagSize)));

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
            SecureByteStringInterop.SecureCopyWithCleanup(metadata.Nonce, out nonce);

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
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.AesGcmDecryptionFailedMessage, cryptoEx));
        }
        catch (Exception ex)
        {
            if (ciphertext != null) SodiumInterop.SecureWipe(ciphertext);
            if (tag != null) SodiumInterop.SecureWipe(tag);
            if (plaintext != null) SodiumInterop.SecureWipe(plaintext);
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.AesGcmUnexpectedErrorMessage, ex));
        }
    }

    public static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> CreateFrom(EcliptixSystemIdentityKeys keys,
        EcliptixProtocolConnection connection)
    {

        EcliptixProtocolSystem system = new(keys) { _protocolConnection = connection };
        return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Ok(system);
    }

    public static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> CreateFrom(EcliptixSystemIdentityKeys keys,
        EcliptixProtocolConnection connection, RatchetConfig ratchetConfig)
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

    public void ResetReplayProtection()
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        connection?.NotifyRatchetRotation();
    }


    private static RatchetConfig GetConfigForExchangeType(PubKeyExchangeType exchangeType)
    {
        return RatchetConfig.Default;
    }

    public Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure> ProduceOutboundEnvelopeMaterials(byte[] plainPayload)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));

        return ProduceSingleMessageComponents(plainPayload, connection);
    }

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundEnvelopeFromMaterials(EnvelopeMetadata metadata, byte[] encryptedPayload)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.ProtocolConnectionNotInitializedMessage));

        return ProcessInboundEnvelopeFromMaterials(metadata, encryptedPayload, connection);
    }

    private Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure> ProduceSingleMessageComponents(byte[] plainPayload,
        EcliptixProtocolConnection connection)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        RatchetChainKey? messageKey = null;
        byte[]? nonce = null;
        byte[]? ad = null;
        byte[]? encrypted = null;
        byte[]? newSenderDhPublicKey = null;
        try
        {
            Result<(RatchetChainKey MessageKey, bool IncludeDhKey), EcliptixProtocolFailure> prepResult =
                connection.PrepareNextSendMessage();
            if (prepResult.IsErr)
                return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Err(prepResult.UnwrapErr());

            (RatchetChainKey MessageKey, bool IncludeDhKey) prep = prepResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> nonceResult = connection.GenerateNextNonce();
            if (nonceResult.IsErr)
                return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Err(nonceResult.UnwrapErr());

            nonce = nonceResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> dhKeyResult = GetOptionalSenderDhKey(prep.IncludeDhKey);
            if (dhKeyResult.IsErr)
                return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());

            newSenderDhPublicKey = dhKeyResult.Unwrap();

            if (prep.IncludeDhKey && newSenderDhPublicKey?.Length > ProtocolSystemConstants.ProtocolSystem.EmptyArrayLength)
            {
            }

            messageKey = prep.MessageKey;

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool connectionIsInitiator = connection.IsInitiator();
            ad = connectionIsInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            Result<byte[], EcliptixProtocolFailure> encryptResult =
                Encrypt(messageKey!, nonce, plainPayload, ad, connection);
            if (encryptResult.IsErr)
                return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Err(encryptResult.UnwrapErr());

            encrypted = encryptResult.Unwrap();

            uint requestId = Helpers.GenerateRandomUInt32(true);

            EnvelopeMetadata metadata = EnvelopeBuilder.CreateEnvelopeMetadata(
                requestId,
                ByteString.CopyFrom(nonce),
                messageKey!.Index,
                null,
                EnvelopeType.Request);

            byte[] encryptedPayloadCopy = new byte[encrypted.Length];
            encrypted.CopyTo(encryptedPayloadCopy, 0);

            stopwatch.Stop();

            return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), EcliptixProtocolFailure>.Ok((metadata, encryptedPayloadCopy));
        }
        finally
        {
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
            if (ad != null) SodiumInterop.SecureWipe(ad);
            if (encrypted != null) SodiumInterop.SecureWipe(encrypted);
            if (newSenderDhPublicKey != null) SodiumInterop.SecureWipe(newSenderDhPublicKey);
        }
    }

    private Result<byte[], EcliptixProtocolFailure> ProcessInboundEnvelopeFromMaterials(EnvelopeMetadata metadata, byte[] encryptedPayload,
        EcliptixProtocolConnection connection)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        byte[]? ad = null;
        try
        {
            // Note: DH ratchet already performed by caller (ProcessSingleInboundMessage)
            // if the message came through that path. EnvelopeMetadata no longer contains dh_public_key.

            Result<Unit, EcliptixProtocolFailure> replayCheck = connection.CheckReplayProtection(
                [.. metadata.Nonce],
                metadata.RatchetIndex);
            if (replayCheck.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(replayCheck.UnwrapErr());

            Result<RatchetChainKey, EcliptixProtocolFailure> messageResult =
                connection.ProcessReceivedMessage(metadata.RatchetIndex);
            if (messageResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());

            RatchetChainKey messageKey = messageResult.Unwrap();

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool isInitiator = connection.IsInitiator();
            ad = isInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.IdentityX25519PublicKey);

            Result<byte[], EcliptixProtocolFailure> result = DecryptFromComponents(messageKey, metadata, encryptedPayload, ad, connection);

            stopwatch.Stop();

            return result;
        }
        finally
        {
            if (ad != null) SodiumInterop.SecureWipe(ad);
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> DecryptFromComponents(RatchetChainKey key, EnvelopeMetadata metadata,
        byte[] encryptedPayload, byte[] ad, EcliptixProtocolConnection? connection)
    {
        using IDisposable? timer = connection?.GetProfiler().StartOperation(ProtocolSystemConstants.ProtocolSystem.AesGcmDecryptOperationName);
        ReadOnlySpan<byte> fullCipherSpan = encryptedPayload.AsSpan();
        const int tagSize = Constants.AesGcmTagSize;
        int cipherLength = fullCipherSpan.Length - tagSize;

        if (cipherLength < ProtocolSystemConstants.ProtocolSystem.CipherLengthMinimumThreshold)
            return Result<byte[], EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.BufferTooSmall(
                string.Format(ProtocolSystemConstants.ProtocolSystem.CiphertextTooSmallMessage, fullCipherSpan.Length, tagSize)));

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

            SecureByteStringInterop.SecureCopyWithCleanup(metadata.Nonce, out nonce);

            plaintext = new byte[ciphertext.Length];

            using (AesGcm aesGcm = new(keySpan, Constants.AesGcmTagSize))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, ad);
            }

            byte[] result = new byte[plaintext.Length];
            plaintext.CopyTo(result, 0);

            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
        }
        catch (Exception ex)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.AesGcmDecryptionFailedMessage, ex));
        }
        finally
        {
            if (ciphertext != null) SodiumInterop.SecureWipe(ciphertext);
            if (tag != null) SodiumInterop.SecureWipe(tag);
            if (plaintext != null) SodiumInterop.SecureWipe(plaintext);
            if (nonce != null) SodiumInterop.SecureWipe(nonce);
        }
    }
}