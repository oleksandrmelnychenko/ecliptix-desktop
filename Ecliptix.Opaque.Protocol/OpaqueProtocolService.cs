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

public class OpaqueProtocolService(AsymmetricKeyParameter staticPublicKey)
{
    public static Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> CreateOprfRequest(byte[] secureKey)
    {
        BigInteger blind = OpaqueCryptoUtilities.GenerateRandomScalar();
        Result<ECPoint, OpaqueFailure> hashResult = OpaqueCryptoUtilities.HashToPoint(secureKey);
        if (hashResult.IsErr) return Result<(byte[], BigInteger), OpaqueFailure>.Err(hashResult.UnwrapErr());

        ECPoint oprfRequestPoint = hashResult.Unwrap().Multiply(blind);
        return Result<(byte[], BigInteger), OpaqueFailure>.Ok((oprfRequestPoint.GetEncoded(true), blind));
    }

    public static Result<byte[], OpaqueFailure> CreateRegistrationRecord(SecureStringHandler password, byte[] oprfResponse,
        BigInteger blind)
    {
        return password.UseBytes(passwordBytes =>
                CreateRegistrationRecord(passwordBytes.ToArray(), oprfResponse, blind))
            .MapErr(sodiumFailure => OpaqueFailure.InvalidInput($"Secure string operation failed: {sodiumFailure.Message}"))
            .Bind(result => result);
    }

    public static Result<byte[], OpaqueFailure> CreateRegistrationRecord(byte[] password, byte[] oprfResponse,
        BigInteger blind)
    {
        using var passwordMemory = ScopedSecureMemory.Wrap(password, clearOnDispose: false);
        byte[]? oprfKey = null;
        byte[]? credentialKey = null;
        byte[]? clientStaticPrivateKey = null;

        try
        {
            oprfKey = RecoverOprfKey(oprfResponse, blind);
            credentialKey = OpaqueCryptoUtilities.DeriveKey(oprfKey, null, CredentialKeyInfo, DefaultKeyLength);

            AsymmetricCipherKeyPair clientStaticKeyPair = OpaqueCryptoUtilities.GenerateKeyPair();
            clientStaticPrivateKey = ((ECPrivateKeyParameters)clientStaticKeyPair.Private).D.ToByteArrayUnsigned();
            byte[] clientStaticPublicKey = ((ECPublicKeyParameters)clientStaticKeyPair.Public).Q.GetEncoded(true);

            Result<byte[], OpaqueFailure> encryptResult =
                OpaqueCryptoUtilities.Encrypt(clientStaticPrivateKey, credentialKey, passwordMemory.AsSpan().ToArray());
            if (encryptResult.IsErr) return Result<byte[], OpaqueFailure>.Err(encryptResult.UnwrapErr());

            byte[] envelope = encryptResult.Unwrap();
            byte[] registrationRecord = new byte[clientStaticPublicKey.Length + envelope.Length];
            clientStaticPublicKey.CopyTo(registrationRecord, 0);
            envelope.CopyTo(registrationRecord, clientStaticPublicKey.Length);

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

    public Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[] TranscriptHash),
            OpaqueFailure>
        CreateSignInFinalizationRequest(string phoneNumber, SecureStringHandler password,
            OpaqueSignInInitResponse signInResponse, BigInteger blind)
    {
        return password.UseBytes(passwordBytes =>
                CreateSignInFinalizationRequest(phoneNumber, passwordBytes.ToArray(), signInResponse, blind))
            .MapErr(sodiumFailure => OpaqueFailure.InvalidInput($"Secure string operation failed: {sodiumFailure.Message}"))
            .Bind(result => result);
    }

    public Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[] TranscriptHash),
            OpaqueFailure>
        CreateSignInFinalizationRequest(string phoneNumber, byte[] passwordBytes,
            OpaqueSignInInitResponse signInResponse, BigInteger blind)
    {
        using var passwordMemory = ScopedSecureMemory.Wrap(passwordBytes, clearOnDispose: false);
        byte[]? oprfKey = null;
        byte[]? credentialKey = null;
        byte[]? clientStaticPrivateKeyBytes = null;
        byte[]? akeResult = null;

        try
        {
            oprfKey = UnsafeMemoryHelpers.WithByteStringAsSpan(signInResponse.ServerOprfResponse,
                span => RecoverOprfKey(span, blind));
            credentialKey = OpaqueCryptoUtilities.DeriveKey(oprfKey, null, CredentialKeyInfo, DefaultKeyLength);

            if (signInResponse.RegistrationRecord.Length < CompressedPublicKeyLength)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.InvalidInput("Invalid registration record: too short."));

            ReadOnlySpan<byte> registrationRecordSpan = signInResponse.RegistrationRecord.Span;
            byte[] clientStaticPublicKeyBytes = registrationRecordSpan[..CompressedPublicKeyLength].ToArray();
            byte[] envelope = registrationRecordSpan[CompressedPublicKeyLength..].ToArray();

            Result<byte[], OpaqueFailure> decryptResult =
                OpaqueCryptoUtilities.Decrypt(envelope, credentialKey, passwordMemory.AsSpan().ToArray());
            if (decryptResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[]), OpaqueFailure>.Err(
                    decryptResult.UnwrapErr());

            clientStaticPrivateKeyBytes = decryptResult.Unwrap();
            ECPrivateKeyParameters clientStaticPrivateKey = new(new BigInteger(1, clientStaticPrivateKeyBytes),
                OpaqueCryptoUtilities.DomainParams);

            AsymmetricCipherKeyPair clientEphemeralKeys = OpaqueCryptoUtilities.GenerateKeyPair();
            ECPoint? serverStaticPublicKey = ((ECPublicKeyParameters)staticPublicKey).Q;
            ECPoint? serverEphemeralPublicKey = UnsafeMemoryHelpers.WithByteStringAsSpan(
                signInResponse.ServerEphemeralPublicKey,
                span => OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(span.ToArray()));
            akeResult = PerformClientAke(clientEphemeralKeys, clientStaticPrivateKey, serverStaticPublicKey,
                serverEphemeralPublicKey);

            byte[] clientEphemeralPublicKeyBytes = ((ECPublicKeyParameters)clientEphemeralKeys.Public).Q.GetEncoded(true);
            byte[] serverStaticPublicKeyBytes = ((ECPublicKeyParameters)staticPublicKey).Q.GetEncoded(true);

            byte[] serverOprfResponseBytes = signInResponse.ServerOprfResponse.ToByteArray();
            byte[] serverEphemeralPublicKeyBytes = signInResponse.ServerEphemeralPublicKey.ToByteArray();

            byte[] transcriptHash = HashTranscript(phoneNumber, serverOprfResponseBytes, clientStaticPublicKeyBytes,
                clientEphemeralPublicKeyBytes, serverStaticPublicKeyBytes, serverEphemeralPublicKeyBytes);

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

    public Result<byte[], OpaqueFailure> VerifyServerMacAndGetSessionKey(
        OpaqueSignInFinalizeResponse response, byte[] sessionKey, byte[] serverMacKey, byte[] transcriptHash)
    {
        byte[] expectedServerMac = CreateMac(serverMacKey, transcriptHash);
        if (!UnsafeMemoryHelpers.WithByteStringAsSpan(response.ServerMac,
            span => CryptographicOperations.FixedTimeEquals(expectedServerMac, span)))
            return Result<byte[], OpaqueFailure>.Err(
                OpaqueFailure.MacVerificationFailed("Server MAC verification failed."));

        return Result<byte[], OpaqueFailure>.Ok(sessionKey);
    }

    private static byte[] RecoverOprfKey(ReadOnlySpan<byte> oprfResponse, BigInteger blind)
    {
        ECPoint oprfResponsePoint = OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(oprfResponse.ToArray());
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

    private byte[] HashTranscript(string phoneNumber, byte[] oprfResponse,
        byte[] clientStaticPublicKey,
        byte[] clientEphemeralPublicKey, byte[] serverStaticPublicKey,
        byte[] serverEphemeralPublicKey)
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