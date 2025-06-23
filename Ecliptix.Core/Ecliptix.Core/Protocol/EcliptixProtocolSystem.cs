using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using Ecliptix.Core.Protocol.Failures;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Ecliptix.Core.Protocol;

public class EcliptixProtocolSystem(EcliptixSystemIdentityKeys ecliptixSystemIdentityKeys) : IDisposable
{
    private EcliptixProtocolConnection? _connectSession;

    public void Dispose()
    {
        _connectSession?.Dispose();
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
        return ecliptixSystemIdentityKeys.CreatePublicBundle()
            .AndThen(bundle => EcliptixProtocolConnection.Create(connectId, true)
                .AndThen(session =>
                {
                    _connectSession = session;
                    return session.GetCurrentSenderDhPublicKey()
                        .Map(dhPublicKey => new PubKeyExchange
                        {
                            State = PubKeyExchangeState.Init,
                            OfType = exchangeType,
                            Payload = bundle.ToProtobufExchange().ToByteString(),
                            InitialDhPublicKey = ByteString.CopyFrom(dhPublicKey)
                        });
                }));
    }

    public Result<PubKeyExchange, EcliptixProtocolFailure> ProcessAndRespondToPubKeyExchange(
        uint connectId, PubKeyExchange peerInitialMessageProto)
    {
        SodiumSecureMemoryHandle? rootKeyHandle = null;
        try
        {
            return Result<Unit, EcliptixProtocolFailure>.Validate(Unit.Value,
                    _ => peerInitialMessageProto.State == PubKeyExchangeState.Init,
                    EcliptixProtocolFailure.InvalidInput(
                        $"Expected peer message state to be Init, but was {peerInitialMessageProto.State}."))
                .AndThen(_ => Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure>.Try(
                    () => Helpers.ParseFromBytes<Protobuf.PubKeyExchange.PublicKeyBundle>(peerInitialMessageProto
                        .Payload.ToByteArray()),
                    ex => EcliptixProtocolFailure.Decode("Failed to parse peer public key bundle from protobuf.", ex)))
                .AndThen(PublicKeyBundle.FromProtobufExchange)
                .AndThen(peerBundle =>
                    EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(peerBundle.IdentityEd25519,
                            peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature)
                        .AndThen(spkValid => Result<Unit, EcliptixProtocolFailure>.Validate(Unit.Value, _ => spkValid,
                            EcliptixProtocolFailure.Handshake("SPK signature validation failed.")))
                        .AndThen(_ =>
                        {
                            ecliptixSystemIdentityKeys.GenerateEphemeralKeyPair();
                            return ecliptixSystemIdentityKeys.CreatePublicBundle();
                        })
                        .AndThen(localBundle => EcliptixProtocolConnection.Create(connectId, false)
                            .AndThen(session =>
                            {
                                _connectSession = session;
                                return ecliptixSystemIdentityKeys.CalculateSharedSecretAsRecipient(
                                        peerBundle.IdentityX25519, peerBundle.EphemeralX25519,
                                        peerBundle.OneTimePreKeys.FirstOrDefault()?.PreKeyId, Constants.X3dhInfo)
                                    .AndThen(derivedKeyHandle =>
                                    {
                                        rootKeyHandle = derivedKeyHandle;
                                        return ReadAndWipeSecureHandle(derivedKeyHandle, Constants.X25519KeySize);
                                    })
                                    .AndThen(rootKeyBytes => session.FinalizeChainAndDhKeys(rootKeyBytes,
                                        peerInitialMessageProto.InitialDhPublicKey.ToByteArray()))
                                    .AndThen(__ => session.SetPeerBundle(peerBundle))
                                    .AndThen(__ => session.SetConnectionState(PubKeyExchangeState.Complete))
                                    .AndThen(__ => session.GetCurrentSenderDhPublicKey())
                                    .Map(dhPublicKey => new PubKeyExchange
                                    {
                                        State = PubKeyExchangeState.Pending,
                                        OfType = peerInitialMessageProto.OfType,
                                        Payload = localBundle.ToProtobufExchange().ToByteString(),
                                        InitialDhPublicKey = ByteString.CopyFrom(dhPublicKey)
                                    });
                            })));
        }
        finally
        {
            rootKeyHandle?.Dispose();
        }
    }

    public Result<Unit, EcliptixProtocolFailure> CompleteDataCenterPubKeyExchange(PubKeyExchange peerMessage)
    {
        SodiumSecureMemoryHandle? rootKeyHandle = null;
        try
        {
            return Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure>.Try(
                    () => Helpers.ParseFromBytes<Protobuf.PubKeyExchange.PublicKeyBundle>(peerMessage.Payload
                        .ToByteArray()),
                    ex => EcliptixProtocolFailure.Decode("Failed to parse peer public key bundle from protobuf.", ex))
                .AndThen(PublicKeyBundle.FromProtobufExchange)
                .AndThen(peerBundle => EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(peerBundle.IdentityEd25519,
                        peerBundle.SignedPreKeyPublic, peerBundle.SignedPreKeySignature)
                    .AndThen(spkValid => Result<Unit, EcliptixProtocolFailure>.Validate(Unit.Value, _ => spkValid,
                        EcliptixProtocolFailure.Handshake("SPK signature validation failed during completion.")))
                    .AndThen(_ => ecliptixSystemIdentityKeys.X3dhDeriveSharedSecret(peerBundle, Constants.X3dhInfo))
                    .AndThen(derivedKeyHandle =>
                    {
                        rootKeyHandle = derivedKeyHandle;
                        return ReadAndWipeSecureHandle(derivedKeyHandle, Constants.X25519KeySize);
                    })
                    .AndThen(rootKeyBytes =>
                        _connectSession!.FinalizeChainAndDhKeys(rootKeyBytes,
                            peerMessage.InitialDhPublicKey.ToByteArray()))
                    .AndThen(_ => _connectSession!.SetPeerBundle(peerBundle))
                    .AndThen(_ => _connectSession!.SetConnectionState(PubKeyExchangeState.Complete)));
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
            return _connectSession!.PrepareNextSendMessage()
                .AndThen(prep => _connectSession.GenerateNextNonce()
                    .AndThen(nonce => GetOptionalSenderDhKey(prep.IncludeDhKey)
                        .AndThen(newSenderDhPublicKey => CloneMessageKey(prep.MessageKey)
                            .AndThen(clonedKey =>
                            {
                                messageKeyClone = clonedKey;
                                return _connectSession.GetPeerBundle();
                            })
                            .AndThen(peerBundle =>
                            {
                                byte[] ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey,
                                    peerBundle.IdentityX25519);
                                return Encrypt(messageKeyClone!, nonce, plainPayload, ad);
                            })
                            .Map(encrypted => new CipherPayload
                            {
                                RequestId = Helpers.GenerateRandomUInt32(true),
                                Nonce = ByteString.CopyFrom(nonce),
                                RatchetIndex = messageKeyClone!.Index,
                                Cipher = ByteString.CopyFrom(encrypted),
                                CreatedAt = GetProtoTimestamp(),
                                DhPublicKey = newSenderDhPublicKey is { Length: > 0 }
                                    ? ByteString.CopyFrom(newSenderDhPublicKey)
                                    : ByteString.Empty
                            }))));
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
            byte[]? receivedDhKey = cipherPayloadProto.DhPublicKey.Length > 0
                ? cipherPayloadProto.DhPublicKey.ToByteArray()
                : null;

            return PerformRatchetIfNeeded(receivedDhKey)
                .AndThen(_ => _connectSession!.ProcessReceivedMessage(cipherPayloadProto.RatchetIndex, receivedDhKey))
                .AndThen(CloneMessageKey)
                .AndThen(clonedKey =>
                {
                    messageKeyClone = clonedKey;
                    return _connectSession!.GetPeerBundle();
                })
                .AndThen(peerBundle =>
                {
                    byte[] ad = CreateAssociatedData(peerBundle.IdentityX25519,
                        ecliptixSystemIdentityKeys.IdentityX25519PublicKey);
                    return Decrypt(messageKeyClone!, cipherPayloadProto, ad);
                });
        }
        finally
        {
            messageKeyClone?.Dispose();
        }
    }

    private Result<Unit, EcliptixProtocolFailure> PerformRatchetIfNeeded(byte[]? receivedDhKey)
    {
        if (receivedDhKey == null) return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

        return _connectSession!.GetCurrentPeerDhPublicKey()
            .AndThen(currentPeerDhKey =>
            {
                if (currentPeerDhKey != null && !receivedDhKey.AsSpan().SequenceEqual(currentPeerDhKey))
                {
                    return _connectSession.PerformReceivingRatchet(receivedDhKey);
                }

                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            });
    }

    private Result<byte[], EcliptixProtocolFailure> GetOptionalSenderDhKey(bool include)
    {
        return include
            ? _connectSession!.GetCurrentSenderDhPublicKey().Map(k => k!)
            : Result<byte[], EcliptixProtocolFailure>.Ok(Array.Empty<byte>());
    }

    private static Result<byte[], EcliptixProtocolFailure> ReadAndWipeSecureHandle(SodiumSecureMemoryHandle handle,
        int size)
    {
        byte[] buffer = new byte[size];
        Result<byte[], EcliptixProtocolFailure> t = handle.Read(buffer).Map(_ =>
        {
            byte[] copy = (byte[])buffer.Clone();
            SodiumInterop.SecureWipe(buffer);
            return copy;
        }).MapSodiumFailure();
        return t;
    }

    private static Result<EcliptixMessageKey, EcliptixProtocolFailure> CloneMessageKey(EcliptixMessageKey key)
    {
        byte[]? keyMaterial = null;
        try
        {
            keyMaterial = ArrayPool<byte>.Shared.Rent(Constants.AesKeySize);
            Span<byte> keySpan = keyMaterial.AsSpan(0, Constants.AesKeySize);
            key.ReadKeyMaterial(keySpan);
            return EcliptixMessageKey.New(key.Index, keySpan);
        }
        finally
        {
            if (keyMaterial != null) ArrayPool<byte>.Shared.Return(keyMaterial, clearArray: true);
        }
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
        byte[]? keyMaterial = null;
        try
        {
            keyMaterial = ArrayPool<byte>.Shared.Rent(Constants.AesKeySize);
            Span<byte> keySpan = keyMaterial.AsSpan(0, Constants.AesKeySize);
            Result<Unit, EcliptixProtocolFailure> readResult = key.ReadKeyMaterial(keySpan);
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());

            (byte[] ciphertext, byte[] tag) = AesGcmService.EncryptAllocating(keySpan, nonce, plaintext, ad);
            byte[] ciphertextAndTag = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, ciphertextAndTag, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, ciphertextAndTag, ciphertext.Length, tag.Length);

            SodiumInterop.SecureWipe(ciphertext);
            SodiumInterop.SecureWipe(tag);
            return Result<byte[], EcliptixProtocolFailure>.Ok(ciphertextAndTag);
        }
        catch (Exception ex)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("AES-GCM encryption failed.", ex));
        }
        finally
        {
            if (keyMaterial != null) ArrayPool<byte>.Shared.Return(keyMaterial, clearArray: true);
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> Decrypt(EcliptixMessageKey key, CipherPayload payload,
        byte[] ad)
    {
        ReadOnlySpan<byte> fullCipherSpan = payload.Cipher.Span;
        int tagSize = Constants.AesGcmTagSize;
        int cipherLength = fullCipherSpan.Length - tagSize;

        if (cipherLength < 0)
            return Result<byte[], EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.BufferTooSmall(
                $"Received ciphertext length ({fullCipherSpan.Length}) is smaller than the GCM tag size ({tagSize})."));

        byte[]? keyMaterial = null;
        byte[]? cipherOnlyBytes = null;
        byte[]? tagBytes = null;
        try
        {
            keyMaterial = ArrayPool<byte>.Shared.Rent(Constants.AesKeySize);
            Span<byte> keySpan = keyMaterial.AsSpan(0, Constants.AesKeySize);
            Result<Unit, EcliptixProtocolFailure> readResult = key.ReadKeyMaterial(keySpan);
            if (readResult.IsErr) return Result<byte[], EcliptixProtocolFailure>.Err(readResult.UnwrapErr());

            cipherOnlyBytes = ArrayPool<byte>.Shared.Rent(cipherLength);
            Span<byte> cipherSpan = cipherOnlyBytes.AsSpan(0, cipherLength);
            fullCipherSpan[..cipherLength].CopyTo(cipherSpan);

            tagBytes = ArrayPool<byte>.Shared.Rent(tagSize);
            Span<byte> tagSpan = tagBytes.AsSpan(0, tagSize);
            fullCipherSpan[cipherLength..].CopyTo(tagSpan);

            byte[] result = AesGcmService.DecryptAllocating(keySpan, payload.Nonce.ToArray(),
                cipherSpan, tagSpan, ad);

            return Result<byte[], EcliptixProtocolFailure>.Ok(result);
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
        finally
        {
            if (keyMaterial != null) ArrayPool<byte>.Shared.Return(keyMaterial, clearArray: true);
            if (cipherOnlyBytes != null) ArrayPool<byte>.Shared.Return(cipherOnlyBytes, clearArray: true);
            if (tagBytes != null) ArrayPool<byte>.Shared.Return(tagBytes, clearArray: true);
        }
    }
}