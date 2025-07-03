using System;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protocol.System.Utilities;
using Google.Protobuf;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using static Ecliptix.Core.OpaqueProtocol.OpaqueConstants;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace Ecliptix.Core.OpaqueProtocol;

public class OpaqueProtocolService(AsymmetricKeyParameter staticPublicKey)
{
    public static Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> CreateOprfRequest(byte[] password)
    {
        BigInteger blind = OpaqueCryptoUtilities.GenerateRandomScalar();
        Result<ECPoint, OpaqueFailure> hashResult = OpaqueCryptoUtilities.HashToPoint(password);
        if (hashResult.IsErr) return Result<(byte[], BigInteger), OpaqueFailure>.Err(hashResult.UnwrapErr());

        ECPoint oprfRequestPoint = hashResult.Unwrap().Multiply(blind);
        return Result<(byte[], BigInteger), OpaqueFailure>.Ok((oprfRequestPoint.GetEncoded(true), blind));
    }

    public static Result<byte[], OpaqueFailure> CreateRegistrationRecord(byte[] password, byte[] oprfResponse,
        BigInteger blind)
    {
        byte[] oprfKey = RecoverOprfKey(oprfResponse, blind);
        byte[] credentialKey = OpaqueCryptoUtilities.DeriveKey(oprfKey, null, CredentialKeyInfo, DefaultKeyLength);

        AsymmetricCipherKeyPair clientStaticKeyPair = OpaqueCryptoUtilities.GenerateKeyPair();
        byte[] clientStaticPrivateKey = ((ECPrivateKeyParameters)clientStaticKeyPair.Private).D.ToByteArrayUnsigned();
        byte[] clientStaticPublicKey = ((ECPublicKeyParameters)clientStaticKeyPair.Public).Q.GetEncoded(true);

        Result<byte[], OpaqueFailure> encryptResult =
            OpaqueCryptoUtilities.Encrypt(clientStaticPrivateKey, credentialKey, password);
        if (encryptResult.IsErr) return Result<byte[], OpaqueFailure>.Err(encryptResult.UnwrapErr());

        byte[] envelope = encryptResult.Unwrap();
        byte[] registrationRecord = new byte[clientStaticPublicKey.Length + envelope.Length];
        clientStaticPublicKey.CopyTo(registrationRecord, 0);
        envelope.CopyTo(registrationRecord, clientStaticPublicKey.Length);

        return Result<byte[], OpaqueFailure>.Ok(registrationRecord);
    }

    public Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[] TranscriptHash),
            OpaqueFailure>
        CreateSignInFinalizationRequest(string phoneNumber, byte[] passwordBytes,
            OpaqueSignInInitResponse signInResponse, BigInteger blind)
    {
        byte[] oprfKey = RecoverOprfKey(signInResponse.ServerOprfResponse.ToByteArray(), blind);
        byte[] credentialKey = OpaqueCryptoUtilities.DeriveKey(oprfKey, null, CredentialKeyInfo, DefaultKeyLength);

        if (signInResponse.RegistrationRecord.Length < CompressedPublicKeyLength)
            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[]), OpaqueFailure>.Err(
                OpaqueFailure.InvalidInput("Invalid registration record: too short."));

        ReadOnlySpan<byte> registrationRecordSpan = signInResponse.RegistrationRecord.ToByteArray();
        byte[] clientStaticPublicKeyBytes = registrationRecordSpan[..CompressedPublicKeyLength].ToArray();
        byte[] envelope = registrationRecordSpan[CompressedPublicKeyLength..].ToArray();

        Result<byte[], OpaqueFailure> decryptResult =
            OpaqueCryptoUtilities.Decrypt(envelope, credentialKey, passwordBytes);
        if (decryptResult.IsErr)
            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[]), OpaqueFailure>.Err(
                decryptResult.UnwrapErr());

        byte[] clientStaticPrivateKeyBytes = decryptResult.Unwrap();
        ECPrivateKeyParameters clientStaticPrivateKey = new(new BigInteger(1, clientStaticPrivateKeyBytes),
            OpaqueCryptoUtilities.DomainParams);

        AsymmetricCipherKeyPair clientEphemeralKeys = OpaqueCryptoUtilities.GenerateKeyPair();
        ECPoint? serverStaticPublicKey = ((ECPublicKeyParameters)staticPublicKey).Q;
        ECPoint? serverEphemeralPublicKey =
            OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(signInResponse.ServerEphemeralPublicKey.ToByteArray());
        byte[] akeResult = PerformClientAke(clientEphemeralKeys, clientStaticPrivateKey, serverStaticPublicKey,
            serverEphemeralPublicKey);

        byte[] clientEphemeralPublicKeyBytes = ((ECPublicKeyParameters)clientEphemeralKeys.Public).Q.GetEncoded(true);
        byte[] serverStaticPublicKeyBytes = ((ECPublicKeyParameters)staticPublicKey).Q.GetEncoded(true);

        byte[] transcriptHash = HashTranscript(phoneNumber, signInResponse.ServerOprfResponse.ToByteArray(),
            clientStaticPublicKeyBytes,
            clientEphemeralPublicKeyBytes, serverStaticPublicKeyBytes,
            signInResponse.ServerEphemeralPublicKey.ToByteArray());

        Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure> keysResult = DeriveFinalKeys(akeResult, transcriptHash);
        if (keysResult.IsErr)
            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[]), OpaqueFailure>.Err(
                keysResult.UnwrapErr());

        (byte[] sessionKey, byte[] clientMacKey, byte[] serverMacKey) = keysResult.Unwrap();
        byte[] clientMac = CreateMac(clientMacKey, transcriptHash);

        OpaqueSignInFinalizeRequest request = new()
        {
            PhoneNumber = phoneNumber,
            ClientEphemeralPublicKey = ByteString.CopyFrom(clientEphemeralPublicKeyBytes),
            ClientMac = ByteString.CopyFrom(clientMac),
            ServerStateToken = signInResponse.ServerStateToken
        };

        return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[]), OpaqueFailure>.Ok((request, sessionKey,
            serverMacKey, transcriptHash));
    }

    public Result<byte[], OpaqueFailure> VerifyServerMacAndGetSessionKey(
        OpaqueSignInFinalizeResponse response, byte[] sessionKey, byte[] serverMacKey, byte[] transcriptHash)
    {
        byte[] expectedServerMac = CreateMac(serverMacKey, transcriptHash);
        if (!CryptographicOperations.FixedTimeEquals(expectedServerMac, response.ServerMac.ToByteArray()))
            return Result<byte[], OpaqueFailure>.Err(
                OpaqueFailure.MacVerificationFailed("Server MAC verification failed."));

        return Result<byte[], OpaqueFailure>.Ok(sessionKey);
    }

    private static byte[] RecoverOprfKey(byte[] oprfResponse, BigInteger blind)
    {
        ECPoint oprfResponsePoint = OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(oprfResponse);
        BigInteger blindInverse = blind.ModInverse(OpaqueCryptoUtilities.DomainParams.N);
        ECPoint finalPoint = oprfResponsePoint.Multiply(blindInverse).Normalize();
        return finalPoint.GetEncoded(true);
    }

    private static byte[] PerformClientAke(AsymmetricCipherKeyPair ephC, ECPrivateKeyParameters statC, ECPoint statSPub,
        ECPoint ephSPub)
    {
        ECPoint dh1 = ephSPub.Multiply(((ECPrivateKeyParameters)ephC.Private).D).Normalize();
        ECPoint dh2 = statSPub.Multiply(((ECPrivateKeyParameters)ephC.Private).D).Normalize();
        ECPoint dh3 = ephSPub.Multiply(statC.D).Normalize();

        byte[] result = new byte[CompressedPublicKeyLength * 3];
        dh1.GetEncoded(true).CopyTo(result, 0);
        dh2.GetEncoded(true).CopyTo(result, CompressedPublicKeyLength);
        dh3.GetEncoded(true).CopyTo(result, CompressedPublicKeyLength * 2);
        return result;
    }

    private byte[] HashTranscript(string phoneNumber, ReadOnlySpan<byte> oprfResponse,
        ReadOnlySpan<byte> clientStaticPublicKey,
        ReadOnlySpan<byte> clientEphemeralPublicKey, ReadOnlySpan<byte> serverStaticPublicKey,
        ReadOnlySpan<byte> serverEphemeralPublicKey)
    {
        Sha256Digest digest = new();

        Update(digest, ProtocolVersion);
        Update(digest, Encoding.UTF8.GetBytes(phoneNumber));
        Update(digest, oprfResponse);
        Update(digest, clientStaticPublicKey);
        Update(digest, clientEphemeralPublicKey);
        Update(digest, serverStaticPublicKey);
        Update(digest, serverEphemeralPublicKey);

        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private static void Update(IDigest digest, ReadOnlySpan<byte> data)
    {
        digest.BlockUpdate(data.ToArray(), 0, data.Length);
    }

    private static Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure> DeriveFinalKeys(
        byte[] akeResult, byte[] transcriptHash)
    {
        Result<byte[], OpaqueFailure> prkResult = OpaqueCryptoUtilities.HkdfExtract(akeResult, AkeSalt);
        if (prkResult.IsErr) return Result<(byte[], byte[], byte[]), OpaqueFailure>.Err(prkResult.UnwrapErr());

        byte[] prk = prkResult.Unwrap();

        Span<byte> infoBuffer = stackalloc byte[SessionKeyInfo.Length + transcriptHash.Length];
        transcriptHash.CopyTo(infoBuffer[SessionKeyInfo.Length..]);

        SessionKeyInfo.CopyTo(infoBuffer[..SessionKeyInfo.Length]);
        byte[] sessionKey = OpaqueCryptoUtilities.HkdfExpand(prk, infoBuffer, MacKeyLength);

        ClientMacKeyInfo.CopyTo(infoBuffer[..ClientMacKeyInfo.Length]);
        byte[] clientMacKey = OpaqueCryptoUtilities.HkdfExpand(prk, infoBuffer, MacKeyLength);

        ServerMacKeyInfo.CopyTo(infoBuffer[..ServerMacKeyInfo.Length]);
        byte[] serverMacKey = OpaqueCryptoUtilities.HkdfExpand(prk, infoBuffer, MacKeyLength);

        return Result<(byte[], byte[], byte[]), OpaqueFailure>.Ok((sessionKey, clientMacKey, serverMacKey));
    }

    private static byte[] CreateMac(byte[] key, byte[] data)
    {
        HMac hmac = new(new Sha256Digest());
        hmac.Init(new KeyParameter(key));
        hmac.BlockUpdate(data, 0, data.Length);
        byte[] mac = new byte[hmac.GetMacSize()];
        hmac.DoFinal(mac, 0);
        return mac;
    }
}