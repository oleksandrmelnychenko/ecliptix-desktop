using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Core;

internal record LocalPublicKeyBundle(
    byte[] IdentityEd25519,
    byte[] IdentityX25519,
    uint SignedPreKeyId,
    byte[] SignedPreKeyPublic,
    byte[] SignedPreKeySignature,
    IReadOnlyList<OneTimePreKeyRecord> OneTimePreKeys,
    byte[]? EphemeralX25519
)
{
    private LocalPublicKeyBundle(InternalBundleData data) : this(
        data.IdentityEd25519,
        data.IdentityX25519,
        data.SignedPreKeyId,
        data.SignedPreKeyPublic,
        data.SignedPreKeySignature,
        data.OneTimePreKeys,
        data.EphemeralX25519)
    {
    }

    public PublicKeyBundle ToProtobufExchange()
    {
        PublicKeyBundle proto = new()
        {
            IdentityPublicKey = ByteString.CopyFrom(IdentityEd25519),
            IdentityX25519PublicKey = ByteString.CopyFrom(IdentityX25519),
            SignedPreKeyId = SignedPreKeyId,
            SignedPreKeyPublicKey = ByteString.CopyFrom(SignedPreKeyPublic),
            SignedPreKeySignature = ByteString.CopyFrom(SignedPreKeySignature)
        };

        if (EphemeralX25519 != null)
        {
            proto.EphemeralX25519PublicKey = ByteString.CopyFrom(EphemeralX25519);
        }

        foreach (OneTimePreKeyRecord opkRecord in OneTimePreKeys)
        {
            proto.OneTimePreKeys.Add(new PublicKeyBundle.Types.OneTimePreKey
            {
                PreKeyId = opkRecord.PreKeyId,
                PublicKey = ByteString.CopyFrom(opkRecord.PublicKey)
            });
        }

        return proto;
    }

    public static Result<LocalPublicKeyBundle, EcliptixProtocolFailure> FromProtobufExchange(
        PublicKeyBundle? proto)
    {
        if (proto == null)
        {
            return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(EcliptixProtocolFailureMessages.PublicKeyBundle.INPUT_PROTOBUF_BUNDLE_CANNOT_BE_NULL));
        }

        return Result<LocalPublicKeyBundle, EcliptixProtocolFailure>.Try(
            () =>
            {
                SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityPublicKey, out byte[] identityEd25519);
                SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityX25519PublicKey, out byte[] identityX25519);
                SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeyPublicKey, out byte[] signedPreKeyPublic);
                SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeySignature, out byte[] signedPreKeySignature);

                if (identityEd25519.Length != Constants.ED_25519_KEY_SIZE)
                {
                    throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.IDENTITY_ED_25519_INVALID_SIZE, Constants.ED_25519_KEY_SIZE));
                }

                if (identityX25519.Length != Constants.X_25519_KEY_SIZE)
                {
                    throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.IDENTITY_X_25519_INVALID_SIZE, Constants.X_25519_KEY_SIZE));
                }

                Result<Unit, EcliptixProtocolFailure> identityX25519ValidationResult = DhValidator.ValidateX25519PublicKey(identityX25519);
                if (identityX25519ValidationResult.IsErr)
                {
                    throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_IDENTITY_X_25519_KEY, identityX25519ValidationResult.UnwrapErr().Message));
                }

                if (signedPreKeyPublic.Length != Constants.X_25519_KEY_SIZE)
                {
                    throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.SIGNED_PRE_KEY_PUBLIC_INVALID_SIZE, Constants.X_25519_KEY_SIZE));
                }

                Result<Unit, EcliptixProtocolFailure> signedPreKeyValidationResult = DhValidator.ValidateX25519PublicKey(signedPreKeyPublic);
                if (signedPreKeyValidationResult.IsErr)
                {
                    throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_SIGNED_PRE_KEY_PUBLIC_KEY, signedPreKeyValidationResult.UnwrapErr().Message));
                }

                if (signedPreKeySignature.Length != Constants.ED_25519_SIGNATURE_SIZE)
                {
                    throw new ArgumentException(
                        string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.SIGNED_PRE_KEY_SIGNATURE_INVALID_SIZE, Constants.ED_25519_SIGNATURE_SIZE));
                }

                byte[]? ephemeralX25519 = null;
                if (!proto.EphemeralX25519PublicKey.IsEmpty)
                {
                    SecureByteStringInterop.SecureCopyWithCleanup(proto.EphemeralX25519PublicKey, out ephemeralX25519);
                }
                if (ephemeralX25519 != null && ephemeralX25519.Length != Constants.X_25519_KEY_SIZE)
                {
                    throw new ArgumentException(
                        string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.EPHEMERAL_X_25519_INVALID_SIZE, Constants.X_25519_KEY_SIZE));
                }

                if (ephemeralX25519 != null)
                {
                    Result<Unit, EcliptixProtocolFailure> ephemeralValidationResult = DhValidator.ValidateX25519PublicKey(ephemeralX25519);
                    if (ephemeralValidationResult.IsErr)
                    {
                        throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_EPHEMERAL_X_25519_KEY, ephemeralValidationResult.UnwrapErr().Message));
                    }
                }

                List<OneTimePreKeyRecord> opkRecords = new(proto.OneTimePreKeys.Count);
                foreach (PublicKeyBundle.Types.OneTimePreKey pOpk in proto.OneTimePreKeys)
                {
                    SecureByteStringInterop.SecureCopyWithCleanup(pOpk.PublicKey, out byte[] opkPublicKey);
                    Result<OneTimePreKeyRecord, EcliptixProtocolFailure> opkResult =
                        OneTimePreKeyRecord.Create(pOpk.PreKeyId, opkPublicKey);
                    if (opkResult.IsErr)
                    {
                        throw new ArgumentException(
                            string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_ONE_TIME_PRE_KEY, pOpk.PreKeyId, opkResult.UnwrapErr().Message));
                    }

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
                return new LocalPublicKeyBundle(internalData);
            },
            ex => ex switch
            {
                ArgumentException argEx => EcliptixProtocolFailure.Decode(
                    string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.FAILED_TO_CREATE_FROM_PROTOBUF_INVALID_DATA, argEx.Message), argEx),
                _ => EcliptixProtocolFailure.Decode(
                    string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.UNEXPECTED_ERROR_CREATING_FROM_PROTOBUF, ex.Message),
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
