using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Core;

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
                SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityPublicKey, out byte[] identityEd25519);
                SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityX25519PublicKey, out byte[] identityX25519);
                SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeyPublicKey, out byte[] signedPreKeyPublic);
                SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeySignature, out byte[] signedPreKeySignature);

                if (identityEd25519.Length != Constants.Ed25519KeySize)
                    throw new ArgumentException($"IdentityEd25519 key must be {Constants.Ed25519KeySize} bytes.");
                if (identityX25519.Length != Constants.X25519KeySize)
                    throw new ArgumentException($"IdentityX25519 key must be {Constants.X25519KeySize} bytes.");

                Result<Unit, EcliptixProtocolFailure> identityX25519ValidationResult = DhValidator.ValidateX25519PublicKey(identityX25519);
                if (identityX25519ValidationResult.IsErr)
                    throw new ArgumentException($"Invalid IdentityX25519 key: {identityX25519ValidationResult.UnwrapErr().Message}");

                if (signedPreKeyPublic.Length != Constants.X25519KeySize)
                    throw new ArgumentException($"SignedPreKeyPublic key must be {Constants.X25519KeySize} bytes.");

                Result<Unit, EcliptixProtocolFailure> signedPreKeyValidationResult = DhValidator.ValidateX25519PublicKey(signedPreKeyPublic);
                if (signedPreKeyValidationResult.IsErr)
                    throw new ArgumentException($"Invalid SignedPreKeyPublic key: {signedPreKeyValidationResult.UnwrapErr().Message}");
                if (signedPreKeySignature.Length != Constants.Ed25519SignatureSize)
                    throw new ArgumentException(
                        $"SignedPreKeySignature must be {Constants.Ed25519SignatureSize} bytes.");

                byte[]? ephemeralX25519 = null;
                if (!proto.EphemeralX25519PublicKey.IsEmpty)
                {
                    SecureByteStringInterop.SecureCopyWithCleanup(proto.EphemeralX25519PublicKey, out ephemeralX25519);
                }
                if (ephemeralX25519 != null && ephemeralX25519.Length != Constants.X25519KeySize)
                    throw new ArgumentException(
                        $"EphemeralX25519 key must be {Constants.X25519KeySize} bytes if present.");

                if (ephemeralX25519 != null)
                {
                    Result<Unit, EcliptixProtocolFailure> ephemeralValidationResult = DhValidator.ValidateX25519PublicKey(ephemeralX25519);
                    if (ephemeralValidationResult.IsErr)
                        throw new ArgumentException($"Invalid EphemeralX25519 key: {ephemeralValidationResult.UnwrapErr().Message}");
                }

                List<OneTimePreKeyRecord> opkRecords = new(proto.OneTimePreKeys.Count);
                foreach (Protobuf.PubKeyExchange.PublicKeyBundle.Types.OneTimePreKey? pOpk in proto.OneTimePreKeys)
                {
                    SecureByteStringInterop.SecureCopyWithCleanup(pOpk.PublicKey, out byte[] opkPublicKey);
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
                    $"Failed to create LocalPublicKeyBundle from Protobuf due to invalid data: {argEx.Message}", argEx),
                _ => EcliptixProtocolFailure.Decode(
                    $"Unexpected error creating LocalPublicKeyBundle from Protobuf: {ex.Message}",
                    ex)
            }
        );
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