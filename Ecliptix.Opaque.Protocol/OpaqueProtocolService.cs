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
            oprfKey = RecoverOprfKey(oprfResponse, blind);
            
            Result<byte[], OpaqueFailure> stretchResult = OpaqueCryptoUtilities.StretchOprfOutput(oprfKey);
            if (stretchResult.IsErr) return Result<byte[], OpaqueFailure>.Err(stretchResult.UnwrapErr());
            
            byte[] stretchedOprfKey = stretchResult.Unwrap();
            credentialKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, CredentialKeyInfo, DefaultKeyLength);
            byte[] authKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, AuthKeyInfo, DefaultKeyLength);

            AsymmetricCipherKeyPair clientStaticKeyPair = OpaqueCryptoUtilities.GenerateKeyPair();
            clientStaticPrivateKey = ((ECPrivateKeyParameters)clientStaticKeyPair.Private).D.ToByteArrayUnsigned();
            byte[] clientStaticPublicKey = ((ECPublicKeyParameters)clientStaticKeyPair.Public).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);
            byte[] serverStaticPublicKey = ((ECPublicKeyParameters)staticPublicKey).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);
            
            byte[] nonce = new byte[NonceLength];
            RandomNumberGenerator.Fill(nonce);
            
            Result<byte[], OpaqueFailure> envelopeResult = OpaqueCryptoUtilities.CreateEnvelopeMac(
                authKey, nonce, clientStaticPublicKey, serverStaticPublicKey,
                Encoding.UTF8.GetBytes(serverIdentity));
                
            if (envelopeResult.IsErr) return Result<byte[], OpaqueFailure>.Err(envelopeResult.UnwrapErr());

            byte[] envelope = envelopeResult.Unwrap();
            byte[] registrationRecord = new byte[clientStaticPublicKey.Length + envelope.Length];
            clientStaticPublicKey.CopyTo(registrationRecord, 0);
            envelope.CopyTo(registrationRecord, clientStaticPublicKey.Length);

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
            oprfKey = SecureByteStringInterop.WithByteStringAsSpan(signInResponse.ServerOprfResponse,
                span => RecoverOprfKey(span, blind));
                
            Result<byte[], OpaqueFailure> stretchResult = OpaqueCryptoUtilities.StretchOprfOutput(oprfKey);
            if (stretchResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(stretchResult.UnwrapErr());
                
            byte[] stretchedOprfKey = stretchResult.Unwrap();
            credentialKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, CredentialKeyInfo, DefaultKeyLength);
            byte[] authKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, AuthKeyInfo, DefaultKeyLength);
            
            byte[] maskingKey = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, MaskingKeyInfo, DefaultKeyLength);

            Result<byte[], OpaqueFailure> unmaskOprfResult = OpaqueCryptoUtilities.UnmaskResponse(
                signInResponse.ServerOprfResponse.Span, maskingKey);
            if (unmaskOprfResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    unmaskOprfResult.UnwrapErr());
            byte[] unmaskedOprfResponse = unmaskOprfResult.Unwrap();
            
            Result<byte[], OpaqueFailure> unmaskRecordResult = OpaqueCryptoUtilities.UnmaskResponse(
                signInResponse.RegistrationRecord.Span, maskingKey);
            if (unmaskRecordResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    unmaskRecordResult.UnwrapErr());
            byte[] unmaskedRegistrationRecord = unmaskRecordResult.Unwrap();

            if (unmaskedRegistrationRecord.Length < CompressedPublicKeyLength)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.InvalidInput(ErrorMessages.InvalidRegistrationRecordTooShort));

            ReadOnlySpan<byte> registrationRecordSpan = unmaskedRegistrationRecord.AsSpan();
            byte[] clientStaticPublicKeyBytes = registrationRecordSpan[..CompressedPublicKeyLength].ToArray();
            byte[] envelope = registrationRecordSpan[CompressedPublicKeyLength..].ToArray();
            byte[] serverStaticPublicKeyBytes = ((ECPublicKeyParameters)staticPublicKey).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);

            Result<bool, OpaqueFailure> envelopeVerifyResult = OpaqueCryptoUtilities.VerifyEnvelopeMac(
                authKey, envelope, clientStaticPublicKeyBytes, serverStaticPublicKeyBytes,
                Encoding.UTF8.GetBytes(DefaultServerIdentity));
                
            if (envelopeVerifyResult.IsErr)
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    envelopeVerifyResult.UnwrapErr());
                    
            if (!envelopeVerifyResult.Unwrap())
                return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                    OpaqueFailure.MacVerificationFailed(ErrorMessages.EnvelopeMacVerificationFailed));
            
            byte[] privateKeyInfo = OpaqueCryptoUtilities.DeriveKey(stretchedOprfKey, null, PrivateKeyInfo, DefaultKeyLength);
            clientStaticPrivateKeyBytes = OpaqueCryptoUtilities.HkdfExpand(privateKeyInfo, clientStaticPublicKeyBytes, OpaqueConstants.ScalarSize);
            ECPrivateKeyParameters clientStaticPrivateKey = new(new BigInteger(ProtocolIndices.BigIntegerPositiveSign, clientStaticPrivateKeyBytes),
                OpaqueCryptoUtilities.DomainParams);

            AsymmetricCipherKeyPair clientEphemeralKeys = OpaqueCryptoUtilities.GenerateKeyPair();
            ECPoint? serverStaticPublicKey = ((ECPublicKeyParameters)staticPublicKey).Q;
            ECPoint? serverEphemeralPublicKey = SecureByteStringInterop.WithByteStringAsSpan(
                signInResponse.ServerEphemeralPublicKey,
                span => OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(span.ToArray()));
            akeResult = PerformClientAke(clientEphemeralKeys, clientStaticPrivateKey, serverStaticPublicKey,
                serverEphemeralPublicKey);

            byte[] clientEphemeralPublicKeyBytes = ((ECPublicKeyParameters)clientEphemeralKeys.Public).Q.GetEncoded(CryptographicFlags.CompressedPointEncoding);

            byte[] serverEphemeralPublicKeyBytes = signInResponse.ServerEphemeralPublicKey.ToByteArray();

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
                PhoneNumber = phoneNumber,
                ClientEphemeralPublicKey = ByteString.CopyFrom(clientEphemeralPublicKeyBytes),
                ClientMac = ByteString.CopyFrom(clientMac),
                ServerStateToken = signInResponse.ServerStateToken
            };

            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Ok((request, sessionKey,
                serverMacKey, transcriptHash, exportKey));
        }
        catch (OpaqueAuthenticationException)
        {
            return Result<(OpaqueSignInFinalizeRequest, byte[], byte[], byte[], byte[]), OpaqueFailure>.Err(
                OpaqueFailure.MacVerificationFailed("Invalid credentials provided."));
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

    private static byte[] RecoverOprfKey(ReadOnlySpan<byte> oprfResponse, BigInteger blind)
    {
        try
        {
            ECPoint oprfResponsePoint = OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(oprfResponse.ToArray());
            Result<Unit, OpaqueFailure> validationResult = OpaqueCryptoUtilities.ValidatePoint(oprfResponsePoint);
            if (validationResult.IsErr)
                throw new OpaqueAuthenticationException("Invalid credentials provided.");
                
            BigInteger blindInverse = blind.ModInverse(OpaqueCryptoUtilities.DomainParams.N);
            ECPoint finalPoint = oprfResponsePoint.Multiply(blindInverse).Normalize();
            return finalPoint.GetEncoded(CryptographicFlags.CompressedPointEncoding);
        }
        catch (Exception ex) when (ex is not OpaqueAuthenticationException)
        {
            throw new OpaqueAuthenticationException("Invalid credentials provided.");
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
        ECPoint dh2 = statSPub.Multiply(((ECPrivateKeyParameters)ephC.Private).D).Normalize();
        ECPoint dh3 = ephSPub.Multiply(statC.D).Normalize();

        byte[] result = new byte[CompressedPublicKeyLength * ProtocolIndices.DhTripleCount];
        dh1.GetEncoded(CryptographicFlags.CompressedPointEncoding).CopyTo(result, CompressedPublicKeyLength * ProtocolIndices.DhFirstOffset);
        dh2.GetEncoded(CryptographicFlags.CompressedPointEncoding).CopyTo(result, CompressedPublicKeyLength * ProtocolIndices.DhSecondOffset);
        dh3.GetEncoded(CryptographicFlags.CompressedPointEncoding).CopyTo(result, CompressedPublicKeyLength * ProtocolIndices.DhThirdOffset);
        return result;
    }

    private static byte[] HashTranscript(string phoneNumber, byte[] oprfResponse,
        byte[] clientStaticPublicKey, byte[] clientEphemeralPublicKey, 
        byte[] serverStaticPublicKey, byte[] serverEphemeralPublicKey,
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