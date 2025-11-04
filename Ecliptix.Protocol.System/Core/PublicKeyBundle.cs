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
                byte[] identityEd25519 = ExtractAndValidateIdentityEd25519(proto);
                byte[] identityX25519 = ExtractAndValidateIdentityX25519(proto);
                byte[] signedPreKeyPublic = ExtractAndValidateSignedPreKey(proto);
                byte[] signedPreKeySignature = ExtractAndValidateSignature(proto);
                byte[]? ephemeralX25519 = ExtractAndValidateEphemeral(proto);
                List<OneTimePreKeyRecord> opkRecords = ExtractAndValidateOneTimePreKeys(proto);

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

    private static byte[] ExtractAndValidateIdentityEd25519(PublicKeyBundle proto)
    {
        SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityPublicKey, out byte[] identityEd25519);

        if (identityEd25519.Length != Constants.ED_25519_KEY_SIZE)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.IDENTITY_ED_25519_INVALID_SIZE, Constants.ED_25519_KEY_SIZE));
        }

        return identityEd25519;
    }

    private static byte[] ExtractAndValidateIdentityX25519(PublicKeyBundle proto)
    {
        SecureByteStringInterop.SecureCopyWithCleanup(proto.IdentityX25519PublicKey, out byte[] identityX25519);

        if (identityX25519.Length != Constants.X_25519_KEY_SIZE)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.IDENTITY_X_25519_INVALID_SIZE, Constants.X_25519_KEY_SIZE));
        }

        Result<Unit, EcliptixProtocolFailure> validationResult = DhValidator.ValidateX25519PublicKey(identityX25519);
        if (validationResult.IsErr)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_IDENTITY_X_25519_KEY, validationResult.UnwrapErr().Message));
        }

        return identityX25519;
    }

    private static byte[] ExtractAndValidateSignedPreKey(PublicKeyBundle proto)
    {
        SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeyPublicKey, out byte[] signedPreKeyPublic);

        if (signedPreKeyPublic.Length != Constants.X_25519_KEY_SIZE)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.SIGNED_PRE_KEY_PUBLIC_INVALID_SIZE, Constants.X_25519_KEY_SIZE));
        }

        Result<Unit, EcliptixProtocolFailure> validationResult = DhValidator.ValidateX25519PublicKey(signedPreKeyPublic);
        if (validationResult.IsErr)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_SIGNED_PRE_KEY_PUBLIC_KEY, validationResult.UnwrapErr().Message));
        }

        return signedPreKeyPublic;
    }

    private static byte[] ExtractAndValidateSignature(PublicKeyBundle proto)
    {
        SecureByteStringInterop.SecureCopyWithCleanup(proto.SignedPreKeySignature, out byte[] signedPreKeySignature);

        if (signedPreKeySignature.Length != Constants.ED_25519_SIGNATURE_SIZE)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.SIGNED_PRE_KEY_SIGNATURE_INVALID_SIZE, Constants.ED_25519_SIGNATURE_SIZE));
        }

        return signedPreKeySignature;
    }

    private static byte[]? ExtractAndValidateEphemeral(PublicKeyBundle proto)
    {
        if (proto.EphemeralX25519PublicKey.IsEmpty)
        {
            return null;
        }

        SecureByteStringInterop.SecureCopyWithCleanup(proto.EphemeralX25519PublicKey, out byte[] ephemeralX25519);

        if (ephemeralX25519.Length != Constants.X_25519_KEY_SIZE)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.EPHEMERAL_X_25519_INVALID_SIZE, Constants.X_25519_KEY_SIZE));
        }

        Result<Unit, EcliptixProtocolFailure> validationResult = DhValidator.ValidateX25519PublicKey(ephemeralX25519);
        if (validationResult.IsErr)
        {
            throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_EPHEMERAL_X_25519_KEY, validationResult.UnwrapErr().Message));
        }

        return ephemeralX25519;
    }

    private static List<OneTimePreKeyRecord> ExtractAndValidateOneTimePreKeys(PublicKeyBundle proto)
    {
        List<OneTimePreKeyRecord> opkRecords = new(proto.OneTimePreKeys.Count);

        foreach (PublicKeyBundle.Types.OneTimePreKey pOpk in proto.OneTimePreKeys)
        {
            SecureByteStringInterop.SecureCopyWithCleanup(pOpk.PublicKey, out byte[] opkPublicKey);
            Result<OneTimePreKeyRecord, EcliptixProtocolFailure> opkResult =
                OneTimePreKeyRecord.Create(pOpk.PreKeyId, opkPublicKey);

            if (opkResult.IsErr)
            {
                throw new ArgumentException(string.Format(EcliptixProtocolFailureMessages.PublicKeyBundle.INVALID_ONE_TIME_PRE_KEY, pOpk.PreKeyId, opkResult.UnwrapErr().Message));
            }

            opkRecords.Add(opkResult.Unwrap());
        }

        return opkRecords;
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
