using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using static Ecliptix.Opaque.Protocol.OpaqueConstants;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace Ecliptix.Opaque.Protocol;

public sealed class OpaqueProtocolService(AsymmetricKeyParameter staticPublicKey)
{
    public static Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> CreateOprfRequest(byte[] secureKey)
    {
        BigInteger blind = OpaqueCryptoUtilities.GenerateRandomScalar();
        Result<ECPoint, OpaqueFailure> hashResult = OpaqueCryptoUtilities.HashToPoint(secureKey);
        if (hashResult.IsErr) return Result<(byte[], BigInteger), OpaqueFailure>.Err(hashResult.UnwrapErr());

        ECPoint oprfRequestPoint = hashResult.Unwrap().Multiply(blind);
        return Result<(byte[], BigInteger), OpaqueFailure>.Ok((oprfRequestPoint.GetEncoded(CryptographicFlags.CompressedPointEncoding), blind));
    }

    public Result<byte[], OpaqueFailure> CreateRegistrationRecord(byte[] password, byte[] oprfResponse,
        BigInteger blind, string serverIdentity = DefaultServerIdentity)
    {
        using ScopedSecureMemory passwordMemory = ScopedSecureMemory.Wrap(password, CryptographicFlags.ClearOnDispose);
        byte[]? oprfKey = null;
        byte[]? credentialKey = null;
        byte[]? clientStaticPrivateKey = null;

        try
        {
            Result<byte[], OpaqueFailure> oprfKeyResult = RecoverOprfKey(oprfResponse, blind);
            if (oprfKeyResult.IsErr) return Result<byte[], OpaqueFailure>.Err(oprfKeyResult.UnwrapErr());
            oprfKey = oprfKeyResult.Unwrap();

            Result<byte[], OpaqueFailure> stretchResult = OpaqueCryptoUtilities.StretchOprfOutput(oprfKey);
            if (stretchResult.IsErr) return Result<byte[], OpaqueFailure>.Err(stretchResult.UnwrapErr());

            byte[] stretchedOprfKey = stretchResult.Unwrap();
            credentialKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, Array.Empty<byte>(), CredentialKeyInfo, DefaultKeyLength);
            byte[] authKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, Array.Empty<byte>(), AuthKeyInfo, DefaultKeyLength);

            byte[] privateKeyInfo = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, PrivateKeyInfo, DefaultKeyLength);
            clientStaticPrivateKey = OpaqueCryptoUtilities.HkdfExpand(privateKeyInfo, Array.Empty<byte>(), OpaqueConstants.ScalarSize);

            ECPrivateKeyParameters clientStaticPrivKeyParams = new(new BigInteger(ProtocolIndices.BigIntegerPositiveSign, clientStaticPrivateKey),
                OpaqueCryptoUtilities.DomainParams);
            ECPublicKeyParameters clientStaticPubKeyParams = new(
                OpaqueCryptoUtilities.DomainParams.G.Multiply(clientStaticPrivKeyParams.D).Normalize(),
                OpaqueCryptoUtilities.DomainParams);
            byte[] clientStaticPublicKey = clientStaticPubKeyParams.Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);
            byte[] serverStaticPublicKey = ((ECPublicKeyParameters)staticPublicKey).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);

            byte[] nonce = new byte[NonceLength];
            RandomNumberGenerator.Fill(nonce);

            Result<byte[], OpaqueFailure> envelopeResult = OpaqueCryptoUtilities.CreateEnvelopeMac(
                authKey, nonce, clientStaticPublicKey, serverStaticPublicKey,
                Encoding.UTF8.GetBytes(serverIdentity));

            if (envelopeResult.IsErr) return Result<byte[], OpaqueFailure>.Err(envelopeResult.UnwrapErr());

            byte[] envelope = envelopeResult.Unwrap();
            byte[] registrationRecord = new byte[clientStaticPublicKey.Length + envelope.Length];
            Span<byte> recordSpan = registrationRecord.AsSpan();
            clientStaticPublicKey.CopyTo(recordSpan[..clientStaticPublicKey.Length]);
            envelope.CopyTo(recordSpan[clientStaticPublicKey.Length..]);

            CryptographicOperations.ZeroMemory(stretchedOprfKey);
            CryptographicOperations.ZeroMemory(authKey);

            return Result<byte[], OpaqueFailure>.Ok(registrationRecord);
        }
        finally
        {
            if (oprfKey != null)
                CryptographicOperations.ZeroMemory(oprfKey);
            if (credentialKey != null)
                CryptographicOperations.ZeroMemory(credentialKey);
            if (clientStaticPrivateKey != null)
                CryptographicOperations.ZeroMemory(clientStaticPrivateKey);
        }
    }

    public Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[] TranscriptHash, byte[] ExportKey),
            OpaqueFailure>
        CreateSignInFinalizationRequest(string phoneNumber, byte[] passwordBytes,
            OpaqueSignInInitResponse signInResponse, BigInteger blind)
    {
        using ScopedSecureMemory passwordMemory = ScopedSecureMemory.Wrap(passwordBytes, CryptographicFlags.ClearOnDispose);
        byte[]? oprfKey = null;
        byte[]? credentialKey = null;
        byte[]? clientStaticPrivateKeyBytes = null;
        byte[]? akeResult = null;

        try
        {
            Result<byte[], OpaqueFailure> oprfKeyResult = SecureByteStringInterop.WithByteStringAsSpan(signInResponse.ServerOprfResponse,
                span => RecoverOprfKey(span, blind));
            if (oprfKeyResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(oprfKeyResult.UnwrapErr());
            oprfKey = oprfKeyResult.Unwrap();

            Result<byte[], OpaqueFailure> stretchResult = OpaqueCryptoUtilities.StretchOprfOutput(oprfKey);
            if (stretchResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(stretchResult.UnwrapErr());

            byte[] stretchedOprfKey = stretchResult.Unwrap();
            credentialKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, Array.Empty<byte>(), CredentialKeyInfo, DefaultKeyLength);
            byte[] authKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, Array.Empty<byte>(), AuthKeyInfo, DefaultKeyLength);


            byte[] unmaskedOprfResponse = signInResponse.ServerOprfResponse.ToByteArray();

            if (unmaskedOprfResponse.Length != CompressedPublicKeyLength)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.InvalidInput(string.Format(ErrorMessages.InvalidOprfResponseSize, CompressedPublicKeyLength, unmaskedOprfResponse.Length)));

            byte[] unmaskedRegistrationRecord = signInResponse.RegistrationRecord.ToByteArray();

            if (unmaskedRegistrationRecord.Length < CompressedPublicKeyLength)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.InvalidInput(ErrorMessages.InvalidRegistrationRecordTooShort));

            ReadOnlySpan<byte> registrationRecordSpan = unmaskedRegistrationRecord.AsSpan();

            byte[] clientStaticPublicKeyBytes = registrationRecordSpan[..CompressedPublicKeyLength].ToArray();
            byte[] envelope = registrationRecordSpan[CompressedPublicKeyLength..].ToArray();


            if (clientStaticPublicKeyBytes.Length != CompressedPublicKeyLength)
            {
                if (clientStaticPublicKeyBytes.Length > CompressedPublicKeyLength)
                {
                    clientStaticPublicKeyBytes = clientStaticPublicKeyBytes[..CompressedPublicKeyLength].ToArray();
                }
                else
                {
                    return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                        OpaqueFailure.InvalidInput(string.Format(ErrorMessages.InvalidClientPublicKeyLength, CompressedPublicKeyLength, clientStaticPublicKeyBytes.Length)));
                }
            }

            byte[] serverStaticPublicKeyBytes = ((ECPublicKeyParameters)staticPublicKey).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);

            Result<bool, OpaqueFailure> envelopeVerifyResult = OpaqueCryptoUtilities.VerifyEnvelopeMac(
                authKey, envelope, clientStaticPublicKeyBytes, serverStaticPublicKeyBytes,
                Encoding.UTF8.GetBytes(DefaultServerIdentity));

            if (envelopeVerifyResult.IsErr)
            {
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    envelopeVerifyResult.UnwrapErr());
            }

            bool successStatus = envelopeVerifyResult.Unwrap();

            if (!successStatus)
            {
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.MacVerificationFailed(ErrorMessages.EnvelopeMacVerificationFailed));
            }

            byte[] privateKeyInfo = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, PrivateKeyInfo, DefaultKeyLength);
            clientStaticPrivateKeyBytes = OpaqueCryptoUtilities.HkdfExpand(privateKeyInfo, Array.Empty<byte>(), OpaqueConstants.ScalarSize);
            ECPrivateKeyParameters clientStaticPrivateKey = new(new BigInteger(ProtocolIndices.BigIntegerPositiveSign, clientStaticPrivateKeyBytes),
                OpaqueCryptoUtilities.DomainParams);

            AsymmetricCipherKeyPair clientEphemeralKeys = OpaqueCryptoUtilities.GenerateKeyPair();
            ECPoint? serverStaticPublicKey = ((ECPublicKeyParameters)staticPublicKey).Q;
            ECPoint? serverEphemeralPublicKey;
            try
            {
                serverEphemeralPublicKey = SecureByteStringInterop.WithByteStringAsSpan(
                    signInResponse.ServerEphemeralPublicKey,
                    span => OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(span.ToArray()));
            }
            catch (Exception)
            {
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.MacVerificationFailed(ErrorMessages.InvalidCredentialsProvided));
            }
            akeResult = PerformClientAke(clientEphemeralKeys, clientStaticPrivateKey, serverStaticPublicKey,
                serverEphemeralPublicKey);

            byte[] clientEphemeralPublicKeyBytes = ((ECPublicKeyParameters)clientEphemeralKeys.Public).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);

            ReadOnlySpan<byte> serverEphemeralPublicKeyBytes = signInResponse.ServerEphemeralPublicKey.Span;

            byte[] transcriptHash = HashTranscript(phoneNumber, unmaskedOprfResponse, clientStaticPublicKeyBytes,
                clientEphemeralPublicKeyBytes, serverStaticPublicKeyBytes, serverEphemeralPublicKeyBytes);

            Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure> keysResult = DeriveFinalKeys(akeResult, transcriptHash);
            if (keysResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    keysResult.UnwrapErr());

            (byte[] sessionKey, byte[] clientMacKey, byte[] serverMacKey) = keysResult.Unwrap();
            byte[] clientMac = CreateMac(clientMacKey, transcriptHash);

            Result<byte[], OpaqueFailure> exportKeyResult = OpaqueCryptoUtilities.DeriveExportKey(akeResult, transcriptHash);
            if (exportKeyResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    exportKeyResult.UnwrapErr());

            byte[] exportKey = exportKeyResult.Unwrap();

            OpaqueSignInFinalizeRequest request = new()
            {
                MobileNumber = phoneNumber,
                ClientEphemeralPublicKey = ByteString.CopyFrom(clientEphemeralPublicKeyBytes),
                ClientMac = ByteString.CopyFrom(clientMac),
                ServerStateToken = signInResponse.ServerStateToken
            };

            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Ok((request, sessionKey,
                serverMacKey, transcriptHash, exportKey));
        }
        catch (Exception)
        {
            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                OpaqueFailure.MacVerificationFailed(ErrorMessages.InvalidCredentialsProvided));
        }
        finally
        {
            if (oprfKey != null)
                CryptographicOperations.ZeroMemory(oprfKey);
            if (credentialKey != null)
                CryptographicOperations.ZeroMemory(credentialKey);
            if (clientStaticPrivateKeyBytes != null)
                CryptographicOperations.ZeroMemory(clientStaticPrivateKeyBytes);
            if (akeResult != null)
                CryptographicOperations.ZeroMemory(akeResult);
        }
    }

    public static Result<byte[], OpaqueFailure> VerifyServerMacAndGetSessionKey(
        OpaqueSignInFinalizeResponse response, byte[] sessionKey, byte[] serverMacKey, byte[] transcriptHash)
    {
        byte[] expectedServerMac = CreateMac(serverMacKey, transcriptHash);
        if (!SecureByteStringInterop.WithByteStringAsSpan(response.ServerMac,
            span => CryptographicOperations.FixedTimeEquals(expectedServerMac, span)))
            return Result<byte[], OpaqueFailure>.Err(
                OpaqueFailure.MacVerificationFailed(ErrorMessages.ServerMacVerificationFailed));

        return Result<byte[], OpaqueFailure>.Ok(sessionKey);
    }

    private static Result<byte[], OpaqueFailure> RecoverOprfKey(ReadOnlySpan<byte> oprfResponse, BigInteger blind)
    {
        try
        {
            ECPoint oprfResponsePoint = OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(oprfResponse.ToArray());
            Result<Unit, OpaqueFailure> validationResult = OpaqueCryptoUtilities.ValidatePoint(oprfResponsePoint);
            if (validationResult.IsErr)
                return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.MacVerificationFailed(ErrorMessages.InvalidCredentialsProvided));

            BigInteger blindInverse = blind.ModInverse(OpaqueCryptoUtilities.DomainParams.N);
            ECPoint finalPoint = oprfResponsePoint.Multiply(blindInverse).Normalize();
            return Result<byte[], OpaqueFailure>.Ok(finalPoint.GetEncoded(CryptographicFlags.CompressedPointEncoding));
        }
        catch (Exception)
        {
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.MacVerificationFailed(ErrorMessages.InvalidCredentialsProvided));
        }
    }

    private static byte[] PerformClientAke(AsymmetricCipherKeyPair ephC, ECPrivateKeyParameters statC, ECPoint statSPub,
        ECPoint ephSPub)
    {
        Result<Unit, OpaqueFailure> validationResult = OpaqueCryptoUtilities.ValidatePoint(statSPub);
        if (validationResult.IsErr)
            throw new InvalidOperationException($"{ErrorMessages.InvalidServerStaticPublicKey}{validationResult.UnwrapErr().Message}");

        validationResult = OpaqueCryptoUtilities.ValidatePoint(ephSPub);
        if (validationResult.IsErr)
            throw new InvalidOperationException($"{ErrorMessages.InvalidServerEphemeralPublicKey}{validationResult.UnwrapErr().Message}");

        ECPoint dh1 = ephSPub.Multiply(((ECPrivateKeyParameters)ephC.Private).D).Normalize();
        ECPoint dh2 = ephSPub.Multiply(statC.D).Normalize();
        ECPoint dh3 = statSPub.Multiply(((ECPrivateKeyParameters)ephC.Private).D).Normalize();

        byte[] result = new byte[CompressedPublicKeyLength * ProtocolIndices.DhTripleCount];
        dh1.GetEncoded(CryptographicFlags.CompressedPointEncoding).CopyTo(result, CompressedPublicKeyLength * ProtocolIndices.DhFirstOffset);
        dh2.GetEncoded(CryptographicFlags.CompressedPointEncoding).CopyTo(result, CompressedPublicKeyLength * ProtocolIndices.DhSecondOffset);
        dh3.GetEncoded(CryptographicFlags.CompressedPointEncoding).CopyTo(result, CompressedPublicKeyLength * ProtocolIndices.DhThirdOffset);
        return result;
    }

    private static byte[] HashTranscript(string phoneNumber, ReadOnlySpan<byte> oprfResponse,
        ReadOnlySpan<byte> clientStaticPublicKey, ReadOnlySpan<byte> clientEphemeralPublicKey,
        ReadOnlySpan<byte> serverStaticPublicKey, ReadOnlySpan<byte> serverEphemeralPublicKey,
        string serverIdentity = DefaultServerIdentity)
    {
        using ScopedSecureMemoryCollection memoryCollection = new();

        Sha256Digest digest = new();

        Update(digest, ProtocolVersion);
        Update(digest, Encoding.UTF8.GetBytes(phoneNumber));
        Update(digest, Encoding.UTF8.GetBytes(serverIdentity));
        Update(digest, oprfResponse);
        Update(digest, clientStaticPublicKey);
        Update(digest, serverStaticPublicKey);
        Update(digest, clientEphemeralPublicKey);
        Update(digest, serverEphemeralPublicKey);

        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, OperationOffsets.DigestStartOffset);
        return hash;
    }

    private static void Update(IDigest digest, ReadOnlySpan<byte> data)
    {
        digest.BlockUpdate(data.ToArray(), OperationOffsets.BlockUpdateStartOffset, data.Length);
    }

    private static Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure> DeriveFinalKeys(
        byte[] akeResult, byte[] transcriptHash)
    {
        Result<byte[], OpaqueFailure> prkResult = OpaqueCryptoUtilities.HkdfExtract(akeResult, AkeSalt);
        if (prkResult.IsErr) return Result<(byte[], byte[], byte[]), OpaqueFailure>.Err(prkResult.UnwrapErr());

        byte[] prk = prkResult.Unwrap();

        Span<byte> infoBuffer = stackalloc byte[SessionKeyInfo.Length + transcriptHash.Length];

        SessionKeyInfo.CopyTo(infoBuffer[..SessionKeyInfo.Length]);
        transcriptHash.CopyTo(infoBuffer[SessionKeyInfo.Length..]);
        byte[] sessionKey = OpaqueCryptoUtilities.HkdfExpand(prk, infoBuffer, MacKeyLength);

        infoBuffer.Clear();
        ClientMacKeyInfo.CopyTo(infoBuffer[..ClientMacKeyInfo.Length]);
        byte[] clientMacKey = OpaqueCryptoUtilities.HkdfExpand(prk, infoBuffer[..ClientMacKeyInfo.Length], MacKeyLength);

        infoBuffer.Clear();
        ServerMacKeyInfo.CopyTo(infoBuffer[..ServerMacKeyInfo.Length]);
        byte[] serverMacKey = OpaqueCryptoUtilities.HkdfExpand(prk, infoBuffer[..ServerMacKeyInfo.Length], MacKeyLength);

        return Result<(byte[], byte[], byte[]), OpaqueFailure>.Ok((sessionKey, clientMacKey, serverMacKey));
    }

    private static byte[] CreateMac(byte[] key, byte[] data)
    {
        HMac hmac = new(new Sha256Digest());
        hmac.Init(new KeyParameter(key));
        hmac.BlockUpdate(data, OperationOffsets.BlockUpdateStartOffset, data.Length);
        byte[] mac = new byte[hmac.GetMacSize()];
        hmac.DoFinal(mac, OperationOffsets.MacStartOffset);
        return mac;
    }
}