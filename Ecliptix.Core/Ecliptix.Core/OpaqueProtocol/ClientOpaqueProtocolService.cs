using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.Membership;
using Google.Protobuf;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using ECPoint = Org.BouncyCastle.Math.EC.ECPoint;

namespace Ecliptix.Core.OpaqueProtocol;

public class ClientOpaqueProtocolService(AsymmetricKeyParameter serverStaticPublicKey)
{
    private readonly AsymmetricKeyParameter _serverStaticPublicKey =
        serverStaticPublicKey ?? throw new ArgumentNullException(nameof(serverStaticPublicKey));

    public Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> CreateOprfRequest(byte[] password)
    {
        BigInteger blind = OpaqueCryptoUtilities.GenerateRandomScalar();
        Result<ECPoint, OpaqueFailure> hashResult = OpaqueCryptoUtilities.HashToPoint(password);
        if (hashResult.IsErr)
            return Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure>.Err(hashResult.UnwrapErr());

        ECPoint p = hashResult.Unwrap();
        ECPoint oprfRequestPoint = p.Multiply(blind);
        return Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure>.Ok((oprfRequestPoint.GetEncoded(true),
            blind));
    }

    public Result<byte[], OpaqueFailure> CreateRegistrationRecord(byte[] password, byte[] oprfResponse,
        BigInteger blind)
    {
        byte[] oprfKey = RecoverOprfKey(oprfResponse, blind);
        byte[] credentialKey =
            OpaqueCryptoUtilities.DeriveKey(oprfKey, null, "oprf_key"u8.ToArray(), 32);
        AsymmetricCipherKeyPair clientStaticKeyPair = OpaqueCryptoUtilities.GenerateKeyPair();
        byte[] clientStaticPrivateKey = ((ECPrivateKeyParameters)clientStaticKeyPair.Private).D.ToByteArrayUnsigned();
        byte[] clientStaticPublicKey = ((ECPublicKeyParameters)clientStaticKeyPair.Public).Q.GetEncoded(true);

        Result<byte[], OpaqueFailure> encryptResult =
            OpaqueCryptoUtilities.Encrypt(clientStaticPrivateKey, credentialKey, password);
        if (encryptResult.IsErr)
            return Result<byte[], OpaqueFailure>.Err(encryptResult.UnwrapErr());

        byte[] envelope = encryptResult.Unwrap();
        return Result<byte[], OpaqueFailure>.Ok(clientStaticPublicKey.Concat(envelope).ToArray());
    }

    public Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey,byte[] transcriptHash), OpaqueFailure>
        ComputeSignInFinalization(
            string phoneNumber, byte[] passwordBytes, OpaqueSignInInitResponse signInResponse, BigInteger blind)
    {
        byte[] oprfKey = RecoverOprfKey(signInResponse.ServerOprfResponse.ToByteArray(), blind);
        byte[] credentialKey =
            OpaqueCryptoUtilities.DeriveKey(oprfKey, null, Encoding.UTF8.GetBytes("oprf_key"), 32);

        const int expectedPublicKeyLength = 33;
        if (signInResponse.RegistrationRecord.Length < expectedPublicKeyLength)
            return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey,byte[]), OpaqueFailure>
                .Err(OpaqueFailure.InvalidInput("Invalid registration record: too short."));

        byte[] clientStaticPublicKeyBytes = signInResponse.RegistrationRecord.Take(expectedPublicKeyLength).ToArray();
        byte[] envelope = signInResponse.RegistrationRecord.Skip(expectedPublicKeyLength).ToArray();

        Result<byte[], OpaqueFailure> decryptResult =
            OpaqueCryptoUtilities.Decrypt(envelope, credentialKey, passwordBytes);
        if (decryptResult.IsErr)
            return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey,byte[]), OpaqueFailure>
                .Err(decryptResult.UnwrapErr());

        try
        {
            byte[] clientStaticPrivateKeyBytes = decryptResult.Unwrap();
            ECPrivateKeyParameters clientStaticPrivateKey = new(new BigInteger(1, clientStaticPrivateKeyBytes),
                OpaqueCryptoUtilities.DomainParams);

            AsymmetricCipherKeyPair clientEphemeralKeys = OpaqueCryptoUtilities.GenerateKeyPair();
            ECPoint serverStaticPublicKey = ((ECPublicKeyParameters)_serverStaticPublicKey).Q;
            ECPoint serverEphemeralPublicKey =
                OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(
                    signInResponse.ServerEphemeralPublicKey.ToByteArray());
            byte[] akeResult = PerformClientAke(clientEphemeralKeys, clientStaticPrivateKey, serverStaticPublicKey,
                serverEphemeralPublicKey);

            byte[] clientEphemeralPublicKeyBytes =
                ((ECPublicKeyParameters)clientEphemeralKeys.Public).Q.GetEncoded(true);
            byte[] serverStaticPublicKeyBytes = ((ECPublicKeyParameters)_serverStaticPublicKey).Q.GetEncoded(true);

            byte[] transcriptHash = HashTranscript(phoneNumber, signInResponse.ServerOprfResponse.ToByteArray(),
                clientStaticPublicKeyBytes, clientEphemeralPublicKeyBytes, serverStaticPublicKeyBytes,
                signInResponse.ServerEphemeralPublicKey.ToByteArray());

            
            var keysResult = DeriveFinalKeys(akeResult, transcriptHash);
            if (keysResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey,byte[]),
                        OpaqueFailure>
                    .Err(keysResult.UnwrapErr());

            var (sessionKey, clientMacKey, serverMacKey) = keysResult.Unwrap();
            byte[] clientMac = CreateMac(clientMacKey, transcriptHash);

            var request = new OpaqueSignInFinalizeRequest
            {
                PhoneNumber = phoneNumber,
                ClientEphemeralPublicKey = ByteString.CopyFrom(clientEphemeralPublicKeyBytes),
                ClientMac = ByteString.CopyFrom(clientMac),
                ServerStateToken = signInResponse.ServerStateToken
            };

            return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey,byte[] transcriptHash), OpaqueFailure>
                .Ok((request, sessionKey, serverMacKey,transcriptHash));
        }
        catch (Exception ex)
        {
            return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey,byte[]), OpaqueFailure>
                .Err(OpaqueFailure.OprfHashingFailed($"Sign-in finalization failed: {ex.Message}"));
        }
    }

    public Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey), string> VerifySignInResponseAsync(
        OpaqueSignInFinalizeRequest request, OpaqueSignInFinalizeResponse response, byte[] serverMacKey,byte[] transcriptHash)
    {
        try
        {
            byte[] expectedServerMac = CreateMac(serverMacKey, transcriptHash);
            if (!CryptographicOperations.FixedTimeEquals(expectedServerMac, response.ServerMac.ToByteArray()))
                return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey), string>.Err(
                    "Server MAC verification failed.");

            return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey), string>.Ok((request, null));
        }
        catch (Exception ex)
        {
            return Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey), string>.Err(
                $"Sign-in verification failed: {ex.Message}");
        }
    }

    private byte[] RecoverOprfKey(byte[] oprfResponse, BigInteger blind)
    {
        if (oprfResponse == null || blind == null)
            throw new ArgumentNullException("Invalid OPRF response or blind.");

        ECPoint oprfResponsePoint = OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(oprfResponse);
        BigInteger blindInverse = blind.ModInverse(OpaqueCryptoUtilities.DomainParams.N);
        return oprfResponsePoint.Multiply(blindInverse).GetEncoded(true);
    }

    private byte[] PerformClientAke(AsymmetricCipherKeyPair eph_c, ECPrivateKeyParameters stat_c, ECPoint stat_s_pub,
        ECPoint eph_s_pub)
    {
        ECPoint dh1 = eph_s_pub.Multiply(((ECPrivateKeyParameters)eph_c.Private).D).Normalize();
        ECPoint dh2 = stat_s_pub.Multiply(((ECPrivateKeyParameters)eph_c.Private).D).Normalize();
        ECPoint dh3 = eph_s_pub.Multiply(stat_c.D).Normalize();
        return dh1.GetEncoded(true).Concat(dh2.GetEncoded(true)).Concat(dh3.GetEncoded(true)).ToArray();
    }

    private byte[] HashTranscript(string phoneNumber, byte[] oprfResponse, byte[] clientStaticPublicKey,
        byte[] clientEphemeralPublicKey, byte[] serverStaticPublicKey, byte[] serverEphemeralPublicKey)
    {
        var digest = new Sha256Digest();

        void Update(byte[] data)
        {
            if (data != null)
                digest.BlockUpdate(data, 0, data.Length);
        }

        Update(Encoding.UTF8.GetBytes("Ecliptix-OPAQUE-v1"));
        Update(Encoding.UTF8.GetBytes(phoneNumber));
        Update(oprfResponse);
        Update(clientStaticPublicKey);
        Update(clientEphemeralPublicKey);
        Update(serverStaticPublicKey);
        Update(serverEphemeralPublicKey);

        byte[] hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);
        return hash;
    }

    private Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure> DeriveFinalKeys(
        byte[] akeResult, byte[] transcriptHash)
    {
        if (akeResult == null || transcriptHash == null)
            return Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure>.Err(
                OpaqueFailure.InvalidInput("Input parameters cannot be null."));

        var prkResult = OpaqueCryptoUtilities.HkdfExtract(akeResult, Encoding.UTF8.GetBytes("OPAQUE-AKE-Salt"));
        if (prkResult.IsErr)
            return Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure>.Err(
                prkResult.UnwrapErr());

        byte[] prk = prkResult.Unwrap();
        byte[] sessionKey = OpaqueCryptoUtilities.HkdfExpand(prk,
            Encoding.UTF8.GetBytes("session_key").Concat(transcriptHash).ToArray(), 32);
        byte[] clientMacKey = OpaqueCryptoUtilities.HkdfExpand(prk,
            Encoding.UTF8.GetBytes("client_mac_key").Concat(transcriptHash).ToArray(), 32);
        byte[] serverMacKey = OpaqueCryptoUtilities.HkdfExpand(prk,
            Encoding.UTF8.GetBytes("server_mac_key").Concat(transcriptHash).ToArray(), 32);
        return Result<(byte[] SessionKey, byte[] ClientMacKey, byte[] ServerMacKey), OpaqueFailure>.Ok((sessionKey,
            clientMacKey, serverMacKey));
    }

    private byte[] CreateMac(byte[] key, byte[] data)
    {
        if (key == null || data == null)
            throw new ArgumentNullException("Key or data cannot be null.");

        HMac hmac = new(new Sha256Digest());
        hmac.Init(new KeyParameter(key));
        hmac.BlockUpdate(data, 0, data.Length);
        byte[] mac = new byte[hmac.GetMacSize()];
        hmac.DoFinal(mac, 0);
        return mac;
    }
}