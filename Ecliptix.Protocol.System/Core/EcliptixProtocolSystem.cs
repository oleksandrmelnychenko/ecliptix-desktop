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
        return ecliptixSystemIdentityKeys.CreatePublicBundle()
            .AndThen(bundle =>
            {

                return EcliptixProtocolConnection.Create(connectId, true)
                    .AndThen(session =>
                    {
                        _protocolConnection = session;
                        _protocolConnection.SetEventHandler(_eventHandler);
                        return session.GetCurrentSenderDhPublicKey()
                            .Map(dhPublicKey =>
                            {
                                return new PubKeyExchange
                                {
                                    State = PubKeyExchangeState.Init,
                                    OfType = exchangeType,
                                    Payload = bundle.ToProtobufExchange().ToByteString(),
                                    InitialDhPublicKey = ByteString.CopyFrom(dhPublicKey)
                                };
                            });
                    });
            });
    }

    public void CompleteDataCenterPubKeyExchange(PubKeyExchange peerMessage)
    {

        SodiumSecureMemoryHandle? rootKeyHandle = null;
        try
        {
            Result<Protobuf.PubKeyExchange.PublicKeyBundle, EcliptixProtocolFailure>.Try(
                    () => {
                        UnsafeMemoryHelpers.SecureCopyWithCleanup(peerMessage.Payload, out byte[] payloadBytes);
                        try
                        {
                            return Helpers.ParseFromBytes<Protobuf.PubKeyExchange.PublicKeyBundle>(payloadBytes);
                        }
                        finally
                        {
                            SodiumInterop.SecureWipe(payloadBytes).IgnoreResult();
                        }
                    },
                    ex => EcliptixProtocolFailure.Decode("Failed to parse peer public key bundle from protobuf.", ex))
                .AndThen(PublicKeyBundle.FromProtobufExchange)
                .AndThen(peerBundle =>
                {

                    return EcliptixSystemIdentityKeys.VerifyRemoteSpkSignature(peerBundle.IdentityEd25519,
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
                        {
                            UnsafeMemoryHelpers.SecureCopyWithCleanup(peerMessage.InitialDhPublicKey, out byte[] dhKeyBytes);
                            try
                            {
                                return _protocolConnection!.FinalizeChainAndDhKeys(rootKeyBytes, dhKeyBytes);
                            }
                            finally
                            {
                                SodiumInterop.SecureWipe(dhKeyBytes).IgnoreResult();
                            }
                        })
                        .AndThen(_ =>
                        {
                            return _protocolConnection!.SetPeerBundle(peerBundle);
                        });
                });
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
            return _protocolConnection!.PrepareNextSendMessage()
                .AndThen(prep => _protocolConnection.GenerateNextNonce()
                    .AndThen(nonce => GetOptionalSenderDhKey(prep.IncludeDhKey)
                        .AndThen(newSenderDhPublicKey => CloneMessageKey(prep.MessageKey)
                            .AndThen(clonedKey =>
                            {
                                messageKeyClone = clonedKey;
                                return _protocolConnection.GetPeerBundle();
                            })
                            .AndThen(peerBundle =>
                            {
                                byte[] ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519);
                                return Encrypt(messageKeyClone!, nonce, plainPayload, ad);
                            })
                            .Map(encrypted =>
                            {
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
                                return payload;
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
            byte[]? receivedDhKey = null;
            if (cipherPayloadProto.DhPublicKey.Length > 0)
            {
                UnsafeMemoryHelpers.SecureCopyWithCleanup(cipherPayloadProto.DhPublicKey, out receivedDhKey);
            }

            return PerformRatchetIfNeeded(receivedDhKey)
                .AndThen(_ => _protocolConnection!.ProcessReceivedMessage(cipherPayloadProto.RatchetIndex))
                .AndThen(clonedKey =>
                {
                    messageKeyClone = clonedKey;
                    return _protocolConnection!.GetPeerBundle();
                })
                .AndThen(peerBundle =>
                {
                    byte[] ad = CreateAssociatedData(ecliptixSystemIdentityKeys.IdentityX25519PublicKey, peerBundle.IdentityX25519);
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

        return _protocolConnection!.GetCurrentPeerDhPublicKey()
            .AndThen(currentPeerDhKey =>
            {
                if (currentPeerDhKey != null && receivedDhKey.AsSpan().SequenceEqual(currentPeerDhKey))
                {
                    return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
                }

                return _protocolConnection.PerformReceivingRatchet(receivedDhKey);
            });
    }

    private Result<byte[], EcliptixProtocolFailure> GetOptionalSenderDhKey(bool include)
    {
        return include
            ? _protocolConnection!.GetCurrentSenderDhPublicKey().Map(k => k!)
            : Result<byte[], EcliptixProtocolFailure>.Ok([]);
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
            byte[] tag = new byte[Constants.AesGcmTagSize];

            using (AesGcm aesGcm = new(keySpan, Constants.AesGcmTagSize))
            {
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, ad);
            }

            byte[] result = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, ciphertext.Length, tag.Length);

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
        Result<byte[], EcliptixProtocolFailure> t = handle.Read(buffer).Map(_ =>
        {
            byte[] copy = (byte[])buffer.Clone();
            SodiumInterop.SecureWipe(buffer);
            return copy;
        }).MapSodiumFailure();
        return t;
    }

    public EcliptixProtocolConnection GetConnection()
    {
        if (_protocolConnection == null) throw new InvalidOperationException("Connection has not been established yet.");
        return _protocolConnection;
    }
}