using System.Security.Cryptography;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Ecliptix.Protocol.System.Core;

internal sealed class EcliptixProtocolSystem(EcliptixSystemIdentityKeys ecliptixSystemIdentityKeys)
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
        PubKeyExchangeType EXCHANGE_TYPE)
    {
        ecliptixSystemIdentityKeys.GenerateEphemeralKeyPair();

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> bundleResult =
            ecliptixSystemIdentityKeys.CreatePublicBundle();
        if (bundleResult.IsErr)
        {
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());
        }

        LocalPublicKeyBundle bundle = bundleResult.Unwrap();

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> sessionResult =
            EcliptixProtocolConnection.Create(connectId, true, RatchetConfig.Default, EXCHANGE_TYPE);
        if (sessionResult.IsErr)
        {
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(sessionResult.UnwrapErr());
        }

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
        {
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());
        }

        byte[]? dhPublicKey = dhKeyResult.Unwrap();
        if (dhPublicKey == null)
        {
            return Result<PubKeyExchange, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.PrepareLocal(ProtocolSystemConstants.ProtocolSystem.DH_PUBLIC_KEY_NULL_MESSAGE));
        }

        PubKeyExchange pubKeyExchange = new()
        {
            State = PubKeyExchangeState.Init,
            OfType = EXCHANGE_TYPE,
            Payload = bundle.ToProtobufExchange().ToByteString(),
            InitialDhPublicKey = ByteString.CopyFrom(dhPublicKey)
        };

        return Result<PubKeyExchange, EcliptixProtocolFailure>.Ok(pubKeyExchange);
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteAuthenticatedPubKeyExchange(PubKeyExchange peerMessage,
        byte[] rootKey)
    {
        Result<byte[]?, EcliptixProtocolFailure> ourDhKeyResult = _protocolConnection?.GetCurrentSenderDhPublicKey() ??
                                                                  Result<byte[]?, EcliptixProtocolFailure>.Err(
                                                                      EcliptixProtocolFailure.Generic(
                                                                          ProtocolSystemConstants.ProtocolSystem
                                                                              .NO_CONNECTION_MESSAGE));
        if (ourDhKeyResult.IsOk)
        {
            byte[]? ourDhKey = ourDhKeyResult.Unwrap();
            if (ourDhKey != null)
            {
                Result<bool, SodiumFailure> comparisonResult =
                    SodiumInterop.ConstantTimeEquals(peerMessage.InitialDhPublicKey.Span, ourDhKey);
                if (comparisonResult.IsOk && comparisonResult.Unwrap())
                {
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem
                            .REFLECTION_ATTACK_MESSAGE));
                }
            }
        }

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
                ex => EcliptixProtocolFailure.Decode(ProtocolSystemConstants.ProtocolSystem.PARSE_PROTOBUF_FAILED_MESSAGE,
                    ex));

        if (parseResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(parseResult.UnwrapErr());
        }

        PublicKeyBundle protobufBundle = parseResult.Unwrap();

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> bundleResult =
            LocalPublicKeyBundle.FromProtobufExchange(protobufBundle);
        if (bundleResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(bundleResult.UnwrapErr());
        }

        LocalPublicKeyBundle peerBundle = bundleResult.Unwrap();

        Result<bool, EcliptixProtocolFailure> signatureResult = EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(
            peerBundle.IdentityEd25519, peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature);
        if (signatureResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());
        }

        bool spkValid = signatureResult.Unwrap();
        if (!spkValid)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.SIGNED_PRE_KEY_FAILED_MESSAGE));
        }

        byte[]? dhKeyBytes = null;
        try
        {
            SecureByteStringInterop.SecureCopyWithCleanup(peerMessage.InitialDhPublicKey, out dhKeyBytes);

            Result<Unit, EcliptixProtocolFailure> dhValidationResult =
                DhValidator.ValidateX25519PublicKey(dhKeyBytes);
            if (dhValidationResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(dhValidationResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> finalizeResult =
                _protocolConnection?.FinalizeChainAndDhKeys(rootKey, dhKeyBytes)
                ?? Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem
                        .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));
            if (finalizeResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(finalizeResult.UnwrapErr());
            }

            Result<Unit, EcliptixProtocolFailure> setPeerResult = _protocolConnection?.SetPeerBundle(peerBundle)
                                                                  ?? Result<Unit, EcliptixProtocolFailure>.Err(
                                                                      EcliptixProtocolFailure.Generic(
                                                                          ProtocolSystemConstants.ProtocolSystem
                                                                              .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));
            return setPeerResult.IsErr
                ? Result<Unit, EcliptixProtocolFailure>.Err(setPeerResult.UnwrapErr())
                : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            SodiumInterop.SecureWipe(dhKeyBytes);
        }
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteDataCenterPubKeyExchange(PubKeyExchange peerMessage)
    {
        Result<Unit, EcliptixProtocolFailure> reflectionCheck = CheckReflectionAttack(peerMessage);
        if (reflectionCheck.IsErr)
        {
            return reflectionCheck;
        }

        SodiumSecureMemoryHandle? rootKeyHandle = null;
        try
        {
            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = ParseAndValidatePeerBundle(peerMessage);
            if (peerBundleResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());
            }

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> secretResult = DeriveSharedSecretKey(peerBundle);
            if (secretResult.IsErr)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(secretResult.UnwrapErr());
            }

            rootKeyHandle = secretResult.Unwrap();

            return FinalizeExchange(rootKeyHandle, peerMessage.InitialDhPublicKey, peerBundle);
        }
        finally
        {
            rootKeyHandle?.Dispose();
        }
    }

    private Result<Unit, EcliptixProtocolFailure> CheckReflectionAttack(PubKeyExchange peerMessage)
    {
        Result<byte[]?, EcliptixProtocolFailure> ourDhKeyResult = _protocolConnection?.GetCurrentSenderDhPublicKey() ??
                                                                  Result<byte[]?, EcliptixProtocolFailure>.Err(
                                                                      EcliptixProtocolFailure.Generic(
                                                                          ProtocolSystemConstants.ProtocolSystem
                                                                              .NO_CONNECTION_MESSAGE));
        if (!ourDhKeyResult.IsOk)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        byte[]? ourDhKey = ourDhKeyResult.Unwrap();
        if (ourDhKey == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        Result<bool, SodiumFailure> comparisonResult =
            SodiumInterop.ConstantTimeEquals(peerMessage.InitialDhPublicKey.Span, ourDhKey);

        if (comparisonResult.IsOk && comparisonResult.Unwrap())
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.REFLECTION_ATTACK_MESSAGE));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<LocalPublicKeyBundle, EcliptixProtocolFailure> ParseAndValidatePeerBundle(PubKeyExchange peerMessage)
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
                ex => EcliptixProtocolFailure.Decode(
                    ProtocolSystemConstants.ProtocolSystem.PARSE_PROTOBUF_FAILED_MESSAGE, ex));

        if (parseResult.IsErr)
        {
            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(parseResult.UnwrapErr());
        }

        PublicKeyBundle protobufBundle = parseResult.Unwrap();

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> bundleResult =
            LocalPublicKeyBundle.FromProtobufExchange(protobufBundle);
        if (bundleResult.IsErr)
        {
            return bundleResult;
        }

        LocalPublicKeyBundle peerBundle = bundleResult.Unwrap();

        Result<bool, EcliptixProtocolFailure> signatureResult = EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(
            peerBundle.IdentityEd25519, peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature);
        if (signatureResult.IsErr)
        {
            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(signatureResult.UnwrapErr());
        }

        bool spkValid = signatureResult.Unwrap();
        if (!spkValid)
        {
            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.SIGNED_PRE_KEY_FAILED_MESSAGE));
        }

        return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Ok(peerBundle);
    }

    private Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> DeriveSharedSecretKey(LocalPublicKeyBundle peerBundle)
    {
        return ecliptixSystemIdentityKeys.X3dhDeriveSharedSecret(peerBundle, Constants.X_3DH_INFO);
    }

    private Result<Unit, EcliptixProtocolFailure> FinalizeExchange(
        SodiumSecureMemoryHandle derivedKeyHandle,
        ByteString peerDhPublicKey,
        LocalPublicKeyBundle peerBundle)
    {
        byte[] rootKeyBytes = new byte[Constants.X_25519_KEY_SIZE];
        byte[]? dhKeyBytes = null;

        try
        {
            Result<Unit, EcliptixProtocolFailure> readResult =
                derivedKeyHandle.Read(rootKeyBytes).MapSodiumFailure();
            if (readResult.IsErr)
            {
                return readResult;
            }

            SecureByteStringInterop.SecureCopyWithCleanup(peerDhPublicKey, out dhKeyBytes);

            Result<Unit, EcliptixProtocolFailure> dhValidationResult =
                DhValidator.ValidateX25519PublicKey(dhKeyBytes);
            if (dhValidationResult.IsErr)
            {
                return dhValidationResult;
            }

            Result<Unit, EcliptixProtocolFailure> finalizeResult =
                _protocolConnection?.FinalizeChainAndDhKeys(rootKeyBytes, dhKeyBytes)
                ?? Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem
                        .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));
            if (finalizeResult.IsErr)
            {
                return finalizeResult;
            }

            Result<Unit, EcliptixProtocolFailure> setPeerResult = _protocolConnection?.SetPeerBundle(peerBundle)
                                                                  ?? Result<Unit, EcliptixProtocolFailure>.Err(
                                                                      EcliptixProtocolFailure.Generic(
                                                                          ProtocolSystemConstants.ProtocolSystem
                                                                              .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));

            return setPeerResult.IsErr
                ? setPeerResult
                : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
        finally
        {
            SodiumInterop.SecureWipe(rootKeyBytes);
            SodiumInterop.SecureWipe(dhKeyBytes);
        }
    }

    public Result<SecureEnvelope, EcliptixProtocolFailure> ProduceOutboundEnvelope(byte[] plainPayload)
    {
        if (plainPayload.Length > ProtocolSystemConstants.ProtocolSystem.MAX_PAYLOAD_SIZE)
        {
            return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(
                    $"Payload size ({plainPayload.Length} bytes) exceeds maximum allowed ({ProtocolSystemConstants.ProtocolSystem.MAX_PAYLOAD_SIZE} bytes)"));
        }

        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
        {
            return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem
                    .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));
        }

        return ProduceSingleEnvelope(plainPayload, connection);
    }

    private Result<SecureEnvelope, EcliptixProtocolFailure> ProduceSingleEnvelope(byte[] plainPayload,
        EcliptixProtocolConnection connection)
    {
        byte[]? nonce = null;
        byte[]? ad = null;
        byte[]? encrypted = null;
        byte[]? newSenderDhPublicKey = null;
        byte[]? metadataKey = null;
        byte[]? encryptedMetadata = null;
        try
        {
            Result<(RatchetChainKey RatchetKey, bool IncludeDhKey), EcliptixProtocolFailure> prepResult =
                connection.PrepareNextSendMessage();
            if (prepResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(prepResult.UnwrapErr());
            }

            (RatchetChainKey RatchetKey, bool IncludeDhKey) prep = prepResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> nonceResult = connection.GenerateNextNonce();
            if (nonceResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(nonceResult.UnwrapErr());
            }

            nonce = nonceResult.Unwrap();

            Result<byte[], EcliptixProtocolFailure> dhKeyResult = GetOptionalSenderDhKey(prep.IncludeDhKey);
            if (dhKeyResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(dhKeyResult.UnwrapErr());
            }

            newSenderDhPublicKey = dhKeyResult.Unwrap();

            RatchetChainKey? messageKey = prep.RatchetKey;

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());
            }

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool connectionIsInitiator = connection.IsInitiator();
            ad = connectionIsInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.GetIdentityX25519PublicKeyCopy(),
                    peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519,
                    ecliptixSystemIdentityKeys.GetIdentityX25519PublicKeyCopy());

            Result<byte[], EcliptixProtocolFailure> encryptResult =
                Encrypt(messageKey!, nonce, plainPayload, ad);
            if (encryptResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(encryptResult.UnwrapErr());
            }

            encrypted = encryptResult.Unwrap();

            uint requestId = Helpers.GenerateRandomUInt32(true);

            EnvelopeMetadata metadata = EnvelopeBuilder.CreateEnvelopeMetadata(
                requestId,
                ByteString.CopyFrom(nonce),
                messageKey!.Index);

            Result<byte[], EcliptixProtocolFailure> metadataKeyResult = connection.GetMetadataEncryptionKey();
            if (metadataKeyResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(metadataKeyResult.UnwrapErr());
            }

            metadataKey = metadataKeyResult.Unwrap();

            byte[] metadataNonce = new byte[Constants.AES_GCM_NONCE_SIZE];
            RandomNumberGenerator.Fill(metadataNonce);

            Result<byte[], EcliptixProtocolFailure> encryptMetadataResult =
                EnvelopeBuilder.EncryptMetadata(metadata, metadataKey, metadataNonce, ad);
            if (encryptMetadataResult.IsErr)
            {
                return Result<SecureEnvelope, EcliptixProtocolFailure>.Err(encryptMetadataResult.UnwrapErr());
            }

            encryptedMetadata = encryptMetadataResult.Unwrap();

            SecureEnvelope envelope = new()
            {
                MetaData = ByteString.CopyFrom(encryptedMetadata),
                EncryptedPayload = ByteString.CopyFrom(encrypted),
                HeaderNonce = ByteString.CopyFrom(metadataNonce),
                Timestamp = GetProtoTimestamp(),
                ResultCode = ByteString.CopyFrom(BitConverter.GetBytes((int)EnvelopeResultCode.Success)),
                DhPublicKey = newSenderDhPublicKey is
                { Length: > ProtocolSystemConstants.ProtocolSystem.EMPTY_ARRAY_LENGTH }
                    ? ByteString.CopyFrom(newSenderDhPublicKey)
                    : ByteString.Empty
            };

            return Result<SecureEnvelope, EcliptixProtocolFailure>.Ok(envelope);
        }
        finally
        {
            if (nonce != null)
            {
                SodiumInterop.SecureWipe(nonce);
            }

            if (ad != null)
            {
                SodiumInterop.SecureWipe(ad);
            }

            if (encrypted != null)
            {
                SodiumInterop.SecureWipe(encrypted);
            }

            if (newSenderDhPublicKey != null)
            {
                SodiumInterop.SecureWipe(newSenderDhPublicKey);
            }

            if (metadataKey != null)
            {
                SodiumInterop.SecureWipe(metadataKey);
            }

            if (encryptedMetadata != null)
            {
                Array.Clear(encryptedMetadata);
            }
        }
    }

    public Result<byte[], EcliptixProtocolFailure> ProcessInboundEnvelope(SecureEnvelope secureEnvelope)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem
                    .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));
        }

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
            Result<Unit, EcliptixProtocolFailure> ratchetResult = HandleDhRatchet(secureEnvelope, out dhPublicKey);
            if (ratchetResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr());
            }

            Result<DecryptionMaterials, EcliptixProtocolFailure> materialsResult =
                ExtractDecryptionMaterials(secureEnvelope, connection, out headerNonce, out metadataKey, out ad, out encryptedMetadata);
            if (materialsResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(materialsResult.UnwrapErr());
            }

            Result<EnvelopeMetadata, EcliptixProtocolFailure> metadataResult =
                EnvelopeBuilder.DecryptMetadata(encryptedMetadata!, metadataKey!, headerNonce!, ad!);
            if (metadataResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(metadataResult.UnwrapErr());
            }

            EnvelopeMetadata metadata = metadataResult.Unwrap();
            byte[] encryptedPayload = secureEnvelope.EncryptedPayload.ToByteArray();

            return ProcessInboundEnvelopeFromMaterials(metadata, encryptedPayload, connection);
        }
        finally
        {
            CleanupDecryptionMaterials(dhPublicKey, headerNonce, metadataKey, ad, encryptedMetadata);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> HandleDhRatchet(SecureEnvelope secureEnvelope, out byte[]? dhPublicKey)
    {
        dhPublicKey = null;

        if (secureEnvelope.DhPublicKey == null || secureEnvelope.DhPublicKey.IsEmpty)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        dhPublicKey = secureEnvelope.DhPublicKey.ToByteArray();
        Result<Unit, EcliptixProtocolFailure> ratchetResult = PerformRatchetIfNeeded(dhPublicKey);

        return ratchetResult.IsErr
            ? Result<Unit, EcliptixProtocolFailure>.Err(ratchetResult.UnwrapErr())
            : Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private readonly record struct DecryptionMaterials(byte[] MetadataKey, byte[] Ad);

    private Result<DecryptionMaterials, EcliptixProtocolFailure> ExtractDecryptionMaterials(
        SecureEnvelope secureEnvelope,
        EcliptixProtocolConnection connection,
        out byte[]? headerNonce,
        out byte[]? metadataKey,
        out byte[]? ad,
        out byte[]? encryptedMetadata)
    {
        headerNonce = null;
        metadataKey = null;
        ad = null;
        encryptedMetadata = null;

        if (secureEnvelope.HeaderNonce == null || secureEnvelope.HeaderNonce.IsEmpty ||
            secureEnvelope.HeaderNonce.Length != Constants.AES_GCM_NONCE_SIZE)
        {
            return Result<DecryptionMaterials, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode("Invalid or missing header nonce for metadata decryption"));
        }

        headerNonce = secureEnvelope.HeaderNonce.ToByteArray();

        Result<byte[], EcliptixProtocolFailure> metadataKeyResult = connection.GetMetadataEncryptionKey();
        if (metadataKeyResult.IsErr)
        {
            return Result<DecryptionMaterials, EcliptixProtocolFailure>.Err(metadataKeyResult.UnwrapErr());
        }

        metadataKey = metadataKeyResult.Unwrap();

        Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
        if (peerBundleResult.IsErr)
        {
            return Result<DecryptionMaterials, EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());
        }

        LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

        bool connectionIsInitiator = connection.IsInitiator();
        ad = connectionIsInitiator
            ? CreateAssociatedData(ecliptixSystemIdentityKeys.GetIdentityX25519PublicKeyCopy(), peerBundle.IdentityX25519)
            : CreateAssociatedData(peerBundle.IdentityX25519, ecliptixSystemIdentityKeys.GetIdentityX25519PublicKeyCopy());

        encryptedMetadata = secureEnvelope.MetaData.ToByteArray();

        return Result<DecryptionMaterials, EcliptixProtocolFailure>.Ok(new DecryptionMaterials(metadataKey, ad));
    }

    private static void CleanupDecryptionMaterials(byte[]? dhPublicKey, byte[]? headerNonce, byte[]? metadataKey, byte[]? ad, byte[]? encryptedMetadata)
    {
        if (dhPublicKey != null)
        {
            SodiumInterop.SecureWipe(dhPublicKey);
        }

        if (headerNonce != null)
        {
            SodiumInterop.SecureWipe(headerNonce);
        }

        if (metadataKey != null)
        {
            SodiumInterop.SecureWipe(metadataKey);
        }

        if (ad != null)
        {
            SodiumInterop.SecureWipe(ad);
        }

        if (encryptedMetadata != null)
        {
            Array.Clear(encryptedMetadata);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformRatchetIfNeeded(byte[]? receivedDhKey)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (receivedDhKey == null || connection == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        Result<byte[]?, EcliptixProtocolFailure> currentKeyResult = connection.GetCurrentPeerDhPublicKey();
        if (currentKeyResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(currentKeyResult.UnwrapErr());
        }

        byte[]? currentPeerDhKey = currentKeyResult.Unwrap();

        if (currentPeerDhKey != null)
        {
            Result<bool, SodiumFailure> comparisonResult =
                SodiumInterop.ConstantTimeEquals(receivedDhKey.AsSpan(), currentPeerDhKey);
            if (comparisonResult.IsOk && comparisonResult.Unwrap())
            {
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }
        }

        return PerformAtomicRatchet(receivedDhKey);
    }

    private Result<Unit, EcliptixProtocolFailure> PerformAtomicRatchet(byte[] receivedDhKey)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();
        if (connection == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem
                    .PROTOCOL_CONNECTION_NOT_INITIALIZED_MESSAGE));
        }

        connection.NotifyRatchetRotation();

        Result<Unit, EcliptixProtocolFailure> ratchetResult = connection.PerformReceivingRatchet(receivedDhKey);
        if (ratchetResult.IsErr)
        {
            return ratchetResult;
        }

        connection.NotifyRatchetRotation();
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private Result<byte[], EcliptixProtocolFailure> GetOptionalSenderDhKey(bool include)
    {
        EcliptixProtocolConnection? connection = GetConnectionSafe();

        if (!include || connection == null)
        {
            return Result<byte[], EcliptixProtocolFailure>.Ok([]);
        }

        Result<byte[]?, EcliptixProtocolFailure> keyResult = connection.GetCurrentSenderDhPublicKey();
        if (keyResult.IsErr)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(keyResult.UnwrapErr());
        }

        byte[]? key = keyResult.Unwrap();

        return Result<byte[], EcliptixProtocolFailure>.Ok(key ?? []);
    }

    private static byte[] CreateAssociatedData(byte[] id1, byte[] id2)
    {
        int maxIdLength = ProtocolSystemConstants.ProtocolSystem.MAX_IDENTITY_KEY_LENGTH;
        if (id1.Length > maxIdLength || id2.Length > maxIdLength)
        {
            throw new ArgumentException(
                string.Format(ProtocolSystemConstants.ProtocolSystem.IDENTITY_KEYS_TOO_LARGE_MESSAGE, maxIdLength));
        }

        if (id1.Length + id2.Length > int.MaxValue / ProtocolSystemConstants.ProtocolSystem.INTEGER_OVERFLOW_DIVISOR)
        {
            throw new ArgumentException(ProtocolSystemConstants.ProtocolSystem.INTEGER_OVERFLOW_MESSAGE);
        }

        byte[] ad = new byte[id1.Length + id2.Length];
        Buffer.BlockCopy(id1, ProtocolSystemConstants.ProtocolSystem.BUFFER_COPY_START_OFFSET, ad,
            ProtocolSystemConstants.ProtocolSystem.BUFFER_COPY_START_OFFSET, id1.Length);
        Buffer.BlockCopy(id2, ProtocolSystemConstants.ProtocolSystem.BUFFER_COPY_START_OFFSET, ad, id1.Length, id2.Length);
        return ad;
    }

    private static Result<byte[], EcliptixProtocolFailure> Encrypt(RatchetChainKey key, byte[] nonce,
        byte[] plaintext, byte[] ad)
    {
        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AES_KEY_SIZE);
        byte[]? ciphertext = null;
        try
        {
            Span<byte> keySpan = keyMaterial.AsSpan();
            Result<Unit, EcliptixProtocolFailure> readResult = RatchetChainKey.ReadKeyMaterial(key, keySpan);
            if (readResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());
            }

            ciphertext = new byte[plaintext.Length];
            Span<byte> tag = stackalloc byte[Constants.AES_GCM_TAG_SIZE];

            using (AesGcm aesGcm = new(keySpan, Constants.AES_GCM_TAG_SIZE))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, ad);
            }

            byte[] result = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, ProtocolSystemConstants.ProtocolSystem.BUFFER_COPY_START_OFFSET, result,
                ProtocolSystemConstants.ProtocolSystem.BUFFER_COPY_START_OFFSET, ciphertext.Length);
            tag.CopyTo(result.AsSpan(ciphertext.Length));

            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
        }
        catch (Exception ex)
        {
            if (ciphertext != null)
            {
                SodiumInterop.SecureWipe(ciphertext);
            }

            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.AES_GCM_ENCRYPTION_FAILED_MESSAGE,
                    ex));
        }
    }

    public static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> CreateFrom(EcliptixSystemIdentityKeys keys,
        EcliptixProtocolConnection connection)
    {
        EcliptixProtocolSystem system = new(keys) { _protocolConnection = connection };
        return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Ok(system);
    }

    public EcliptixProtocolConnection? GetConnection() => GetConnectionSafe();

    private Result<byte[], EcliptixProtocolFailure> ProcessInboundEnvelopeFromMaterials(EnvelopeMetadata metadata,
        byte[] encryptedPayload,
        EcliptixProtocolConnection connection)
    {
        byte[]? ad = null;
        try
        {
            Result<Unit, EcliptixProtocolFailure> replayCheck = connection.CheckReplayProtection(
                [.. metadata.Nonce],
                metadata.RatchetIndex);
            if (replayCheck.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(replayCheck.UnwrapErr());
            }

            Result<RatchetChainKey, EcliptixProtocolFailure> messageResult =
                connection.ProcessReceivedMessage(metadata.RatchetIndex);
            if (messageResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());
            }

            RatchetChainKey messageKey = messageResult.Unwrap();

            Result<LocalPublicKeyBundle, EcliptixProtocolFailure> peerBundleResult = connection.GetPeerBundle();
            if (peerBundleResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(peerBundleResult.UnwrapErr());
            }

            LocalPublicKeyBundle peerBundle = peerBundleResult.Unwrap();

            bool isInitiator = connection.IsInitiator();
            ad = isInitiator
                ? CreateAssociatedData(ecliptixSystemIdentityKeys.GetIdentityX25519PublicKeyCopy(),
                    peerBundle.IdentityX25519)
                : CreateAssociatedData(peerBundle.IdentityX25519,
                    ecliptixSystemIdentityKeys.GetIdentityX25519PublicKeyCopy());

            Result<byte[], EcliptixProtocolFailure> result =
                DecryptFromMaterials(messageKey, metadata, encryptedPayload, ad);

            return result;
        }
        finally
        {
            SodiumInterop.SecureWipe(ad);
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> DecryptFromMaterials(RatchetChainKey key,
        EnvelopeMetadata metadata,
        byte[] encryptedPayload, byte[] ad)
    {
        ReadOnlySpan<byte> fullCipherSpan = encryptedPayload.AsSpan();
        const int TAG_SIZE = Constants.AES_GCM_TAG_SIZE;
        int cipherLength = fullCipherSpan.Length - TAG_SIZE;

        if (cipherLength < ProtocolSystemConstants.ProtocolSystem.CIPHER_LENGTH_MINIMUM_THRESHOLD)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.BUFFER_TOO_SMALL(
                string.Format(ProtocolSystemConstants.ProtocolSystem.CIPHERTEXT_TOO_SMALL_MESSAGE, fullCipherSpan.Length,
                    TAG_SIZE)));
        }

        using SecurePooledArray<byte> keyMaterial = SecureArrayPool.Rent<byte>(Constants.AES_KEY_SIZE);
        byte[]? plaintext = null;
        byte[]? nonce = null;

        try
        {
            Span<byte> keySpan = keyMaterial.AsSpan();
            Result<Unit, EcliptixProtocolFailure> readResult = RatchetChainKey.ReadKeyMaterial(key, keySpan);
            if (readResult.IsErr)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());
            }

            ReadOnlySpan<byte> ciphertextSpan = fullCipherSpan[..cipherLength];
            ReadOnlySpan<byte> tagSpan = fullCipherSpan[cipherLength..];

            SecureByteStringInterop.SecureCopyWithCleanup(metadata.Nonce, out nonce);

            plaintext = new byte[cipherLength];

            using (AesGcm aesGcm = new(keySpan, Constants.AES_GCM_TAG_SIZE))
            {
                aesGcm.Decrypt(nonce, ciphertextSpan, tagSpan, plaintext, ad);
            }

            byte[] result = new byte[plaintext.Length];
            plaintext.CopyTo(result, 0);

            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
        }
        catch (Exception ex)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic(ProtocolSystemConstants.ProtocolSystem.AES_GCM_DECRYPTION_FAILED_MESSAGE,
                    ex));
        }
        finally
        {
            if (plaintext != null)
            {
                SodiumInterop.SecureWipe(plaintext);
            }

            if (nonce != null)
            {
                SodiumInterop.SecureWipe(nonce);
            }
        }
    }
}
