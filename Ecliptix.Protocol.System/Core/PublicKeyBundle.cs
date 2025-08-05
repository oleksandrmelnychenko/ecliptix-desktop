using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Core;

public record OneTimePreKeyRecord(uint PreKeyId, byte[] PublicKey)
{
    public static Result<OneTimePreKeyRecord, EcliptixProtocolFailure> Create(uint preKeyId, byte[] publicKey)
    {
        if (publicKey.Length != Constants.X25519PublicKeySize)
            return Result<OneTimePreKeyRecord, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode(
                    $"One-time prekey public key must be {Constants.X25519PublicKeySize} bytes."));

        return Result<OneTimePreKeyRecord, EcliptixProtocolFailure>.Ok(new OneTimePreKeyRecord(preKeyId, publicKey));
    }
}

public record PublicKeyBundle(
    byte[] IdentityEd25519,
    byte[] IdentityX25519,
    uint SignedPreKeyId,
    byte[] SignedPreKeyPublic,
    byte[] SignedPreKeySignature,
    IReadOnlyList<OneTimePreKeyRecord> OneTimePreKeys,
    byte[]? EphemeralX25519
)
{
    private PublicKeyBundle(InternalBundleData data) : this(
        data.IdentityEd25519,
        data.IdentityX25519,
        data.SignedPreKeyId,
        data.SignedPreKeyPublic,
        data.SignedPreKeySignature,
        data.OneTimePreKeys,
        data.EphemeralX25519)
    {
    }

    public Protobuf.PubKeyExchange.PublicKeyBundle ToProtobufExchange()
    {
        Protobuf.PubKeyExchange.PublicKeyBundle proto = new()
        {
            IdentityPublicKey = ByteString.CopyFrom(IdentityEd25519),
            IdentityX25519PublicKey = ByteString.CopyFrom(IdentityX25519),
            SignedPreKeyId = SignedPreKeyId,
            SignedPreKeyPublicKey = ByteString.CopyFrom(SignedPreKeyPublic),
            SignedPreKeySignature = ByteString.CopyFrom(SignedPreKeySignature)
        };

        if (EphemeralX25519 != null) proto.EphemeralX25519PublicKey = ByteString.CopyFrom(EphemeralX25519);

        foreach (OneTimePreKeyRecord opkRecord in OneTimePreKeys)
            proto.OneTimePreKeys.Add(new Protobuf.PubKeyExchange.PublicKeyBundle.Types.OneTimePreKey
            {
                PreKeyId = opkRecord.PreKeyId,
                PublicKey = ByteString.CopyFrom(opkRecord.PublicKey)
            });

        return proto;
    }

    public static Result<PublicKeyBundle, EcliptixProtocolFailure> FromProtobufExchange(
        Protobuf.PubKeyExchange.PublicKeyBundle? proto)
    {
        if (proto == null)
            return Result<PublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Input Protobuf bundle cannot be null."));

        return Result<PublicKeyBundle, EcliptixProtocolFailure>.Try(
            () =>
            {
                byte[] identityEd25519 = proto.IdentityPublicKey.ToByteArray();
                byte[] identityX25519 = proto.IdentityX25519PublicKey.ToByteArray();
                byte[] signedPreKeyPublic = proto.SignedPreKeyPublicKey.ToByteArray();
                byte[] signedPreKeySignature = proto.SignedPreKeySignature.ToByteArray();

                ValidateKeyLength(identityEd25519, Constants.Ed25519PublicKeySize, "IdentityEd25519");
                ValidateKeyLength(identityX25519, Constants.X25519PublicKeySize, "IdentityX25519");
                ValidateKeyLength(signedPreKeyPublic, Constants.X25519PublicKeySize, "SignedPreKeyPublic");
                ValidateKeyLength(signedPreKeySignature, Constants.Ed25519SignatureSize, "SignedPreKeySignature");

                byte[]? ephemeralX25519 = proto.EphemeralX25519PublicKey.IsEmpty
                    ? null
                    : proto.EphemeralX25519PublicKey.ToByteArray();
                if (ephemeralX25519 != null)
                    ValidateKeyLength(ephemeralX25519, Constants.X25519PublicKeySize, "EphemeralX25519");

                const int MaxOpkCount = 100; // Prevent potential DoS from malformed input
                if (proto.OneTimePreKeys.Count > MaxOpkCount)
                    throw new ArgumentException($"Too many one-time prekeys (max {MaxOpkCount}).");

                List<OneTimePreKeyRecord> opkRecords = new(proto.OneTimePreKeys.Count);
                foreach (Protobuf.PubKeyExchange.PublicKeyBundle.Types.OneTimePreKey? pOpk in proto.OneTimePreKeys)
                {
                    byte[] opkPublicKey = pOpk.PublicKey.ToByteArray();
                    Result<OneTimePreKeyRecord, EcliptixProtocolFailure> opkResult =
                        OneTimePreKeyRecord.Create(pOpk.PreKeyId, opkPublicKey);
                    if (opkResult.IsErr)
                        throw new ArgumentException(
                            $"Invalid OneTimePreKey (ID: {pOpk.PreKeyId}): {opkResult.UnwrapErr().Message}");

                    opkRecords.Add(opkResult.Unwrap());
                }

                InternalBundleData internalData = new()
                {
                    IdentityEd25519 = identityEd25519,
                    IdentityX25519 = identityX25519,
                    SignedPreKeyId = proto.SignedPreKeyId,
                    SignedPreKeyPublic = signedPreKeyPublic,
                    SignedPreKeySignature = signedPreKeySignature,
                    OneTimePreKeys = opkRecords,
                    EphemeralX25519 = ephemeralX25519
                };
                return new PublicKeyBundle(internalData);
            },
            ex => ex switch
            {
                ArgumentException argEx => EcliptixProtocolFailure.Decode(
                    $"Failed to create PublicKeyBundle from Protobuf due to invalid data: {argEx.Message}", argEx),
                _ => EcliptixProtocolFailure.Decode(
                    $"Unexpected error creating PublicKeyBundle from Protobuf: {ex.Message}",
                    ex)
            }
        );
    }

    private static void ValidateKeyLength(byte[] key, int expectedLength, string keyName)
    {
        if (key.Length != expectedLength)
            throw new ArgumentException($"{keyName} key must be {expectedLength} bytes.");
    }

    private readonly struct InternalBundleData
    {
        public required byte[] IdentityEd25519 { get; init; }
        public required byte[] IdentityX25519 { get; init; }
        public required uint SignedPreKeyId { get; init; }
        public required byte[] SignedPreKeyPublic { get; init; }
        public required byte[] SignedPreKeySignature { get; init; }
        public required List<OneTimePreKeyRecord> OneTimePreKeys { get; init; }
        public required byte[]? EphemeralX25519 { get; init; }
    }
}
