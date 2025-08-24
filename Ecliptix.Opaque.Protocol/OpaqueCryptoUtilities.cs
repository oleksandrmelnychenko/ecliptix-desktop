using System.Security.Cryptography;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Math.EC;
using Org.BouncyCastle.Security;
using static Ecliptix.Opaque.Protocol.OpaqueConstants;

namespace Ecliptix.Opaque.Protocol;

public static class OpaqueCryptoUtilities
{
    private static readonly X9ECParameters CurveParams = InitializeCurveParams();
    public static readonly ECDomainParameters DomainParams = new(CurveParams.Curve, CurveParams.G, CurveParams.N, CurveParams.H);

    private static X9ECParameters InitializeCurveParams()
    {
        try
        {
            X9ECParameters? curveParams = ECNamedCurveTable.GetByName(CryptographicConstants.EllipticCurveName);
            if (curveParams == null)
                throw new InvalidOperationException($"Elliptic curve '{CryptographicConstants.EllipticCurveName}' not found");
            return curveParams;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize elliptic curve parameters: {ex.Message}", ex);
        }
    }

    private static readonly ThreadLocal<Sha256Digest> DigestPool = new(() => new Sha256Digest(), false);
    private static readonly SecureRandom SecureRandomInstance = InitializeSecureRandom();

    private static SecureRandom InitializeSecureRandom()
    {
        try
        {
            return new SecureRandom();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to initialize SecureRandom for cryptographic operations: {ex.Message}", ex);
        }
    }

    public static Result<byte[], OpaqueFailure> HkdfExtract(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt)
    {
        if (ikm.IsEmpty) return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.InvalidInput());

        HMac hmac = new(new Sha256Digest());
        ReadOnlySpan<byte> effectiveSalt = salt.IsEmpty ? stackalloc byte[hmac.GetMacSize()] : salt;

        try
        {
            hmac.Init(new KeyParameter(effectiveSalt.ToArray()));
            hmac.BlockUpdate(ikm.ToArray(), 0, ikm.Length);
            byte[] prk = new byte[hmac.GetMacSize()];
            hmac.DoFinal(prk, 0);
            return Result<byte[], OpaqueFailure>.Ok(prk);
        }
        catch (Exception ex)
        {
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.InvalidKeySignature(ex.Message, ex));
        }
    }

    private static byte[] HkdfExpand(ReadOnlySpan<byte> prk, ReadOnlySpan<byte> info, int outputLength)
    {
        HkdfBytesGenerator hkdf = new(new Sha256Digest());
        hkdf.Init(HkdfParameters.SkipExtractParameters(prk.ToArray(), info.ToArray()));
        byte[] okm = new byte[outputLength];
        hkdf.GenerateBytes(okm, 0, outputLength);
        return okm;
    }

    public static byte[] HkdfExpand(byte[] prk, ReadOnlySpan<byte> info, int outputLength)
    {
        return HkdfExpand(prk.AsSpan(), info, outputLength);
    }

    private static byte[] DeriveKey(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> info, int outputLength)
    {
        HkdfBytesGenerator hkdf = new(new Sha256Digest());
        byte[]? saltArray = salt.IsEmpty ? null : salt.ToArray();
        hkdf.Init(new HkdfParameters(ikm.ToArray(), saltArray, info.ToArray()));
        byte[] okm = new byte[outputLength];
        hkdf.GenerateBytes(okm, 0, outputLength);
        return okm;
    }

    public static byte[] DeriveKey(byte[] ikm, byte[]? salt, ReadOnlySpan<byte> info, int outputLength) =>
        DeriveKey(ikm.AsSpan(), salt.AsSpan(), info, outputLength);

    private static byte[] DecompressPoint(BigInteger x, byte sign)
    {
        byte[] xBytes = x.ToByteArrayUnsigned();
        byte[] compressed = new byte[xBytes.Length + 1];
        compressed[0] = sign;
        xBytes.AsSpan().CopyTo(compressed.AsSpan(1));
        return compressed;
    }

    public static BigInteger GenerateRandomScalar()
    {
        BigInteger scalar;
        do
        {
            scalar = new BigInteger(DomainParams.N.BitLength, SecureRandomInstance);
        } while (scalar.SignValue <= 0 || scalar.CompareTo(DomainParams.N) >= 0);
        return scalar;
    }

    public static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        ECKeyPairGenerator generator = new();
        generator.Init(new ECKeyGenerationParameters(DomainParams, SecureRandomInstance));
        return generator.GenerateKeyPair();
    }

    public static Result<Unit, OpaqueFailure> ValidatePoint(Org.BouncyCastle.Math.EC.ECPoint point)
    {
        if (point.IsInfinity)
            return Result<Unit, OpaqueFailure>.Err(OpaqueFailure.InvalidPoint(ErrorMessages.PointAtInfinity));

        if (!point.IsValid())
            return Result<Unit, OpaqueFailure>.Err(OpaqueFailure.InvalidPoint(ErrorMessages.PointNotValid));

        Org.BouncyCastle.Math.EC.ECPoint orderCheck = point.Multiply(DomainParams.N);
        if (!orderCheck.IsInfinity)
            return Result<Unit, OpaqueFailure>.Err(OpaqueFailure.SubgroupCheckFailed(ErrorMessages.SubgroupCheckFailed));

        return Result<Unit, OpaqueFailure>.Ok(Unit.Value);
    }

    public static Result<byte[], OpaqueFailure> StretchOprfOutput(ReadOnlySpan<byte> oprfOutput)
    {
        if (oprfOutput.IsEmpty)
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.InvalidInput(ErrorMessages.OprfOutputEmpty));

        try
        {
            using ScopedSecureMemoryCollection memoryCollection = new();

            using ScopedSecureMemory saltBuffer = memoryCollection.Allocate(OpaqueConstants.Pbkdf2SaltLength);
            Span<byte> salt = saltBuffer.AsSpan();

            byte[] saltBytes = HkdfExpand(oprfOutput.ToArray(), HkdfInfoStrings.OpaqueSalt.AsSpan(), Pbkdf2SaltLength);
            saltBytes.AsSpan().CopyTo(salt);

            byte[] saltArray = salt.ToArray();
            using Rfc2898DeriveBytes pbkdf2 = new(
                oprfOutput.ToArray(),
                saltArray,
                Pbkdf2Iterations,
                System.Security.Cryptography.HashAlgorithmName.SHA256
            );
            CryptographicOperations.ZeroMemory(saltArray);

            byte[] stretched = pbkdf2.GetBytes(HashLength);
            return Result<byte[], OpaqueFailure>.Ok(stretched);
        }
        catch (Exception ex)
        {
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.StretchingFailed($"{ErrorMessages.Pbkdf2Failed}{ex.Message}", ex));
        }
    }

    public static Result<Org.BouncyCastle.Math.EC.ECPoint, OpaqueFailure> HashToPoint(ReadOnlySpan<byte> inputBytes)
    {
        Sha256Digest digest = DigestPool.Value!;
        byte counter = 0;
        const int maxAttempts = CryptographicConstants.MaxHashToPointAttempts;

        while (counter < maxAttempts)
        {
            digest.Reset();
            digest.BlockUpdate(inputBytes.ToArray(), 0, inputBytes.Length);
            digest.Update(counter);
            byte[] hash = new byte[digest.GetDigestSize()];
            digest.DoFinal(hash, 0);

            BigInteger x = new(CryptographicConstants.BigIntegerPositiveSign, hash);
            if (x.SignValue > 0 && x.CompareTo(DomainParams.Curve.Field.Characteristic) < 0)
            {
                try
                {
                    Org.BouncyCastle.Math.EC.ECPoint point = DomainParams.Curve.DecodePoint(DecompressPoint(x, CryptographicConstants.PointCompressionPrefix)).Normalize();
                    Result<Unit, OpaqueFailure> validationResult = ValidatePoint(point);
                    if (validationResult.IsOk)
                        return Result<Org.BouncyCastle.Math.EC.ECPoint, OpaqueFailure>.Ok(point);
                }
                catch { }
            }
            counter++;
        }

        return Result<Org.BouncyCastle.Math.EC.ECPoint, OpaqueFailure>.Err(OpaqueFailure.HashingValidPointFailed());
    }

    private static byte[] CreateMac(ReadOnlySpan<byte> key, ReadOnlySpan<byte> data)
    {
        HMac hmac = new(new Sha256Digest());
        hmac.Init(new KeyParameter(key.ToArray()));
        hmac.BlockUpdate(data.ToArray(), 0, data.Length);
        byte[] mac = new byte[hmac.GetMacSize()];
        hmac.DoFinal(mac, 0);
        return mac;
    }

    public static Result<byte[], OpaqueFailure> CreateEnvelopeMac(
        ReadOnlySpan<byte> authKey,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> clientPublicKey,
        ReadOnlySpan<byte> serverPublicKey,
        ReadOnlySpan<byte> serverIdentity)
    {
        try
        {
            using ScopedSecureMemoryCollection memoryCollection = new();

            int totalDataLength = nonce.Length + clientPublicKey.Length + serverPublicKey.Length + serverIdentity.Length;
            using ScopedSecureMemory dataBuffer = memoryCollection.Allocate(totalDataLength);
            Span<byte> data = dataBuffer.AsSpan();

            int offset = 0;
            nonce.CopyTo(data.Slice(offset, nonce.Length));
            offset += nonce.Length;

            clientPublicKey.CopyTo(data.Slice(offset, clientPublicKey.Length));
            offset += clientPublicKey.Length;

            serverPublicKey.CopyTo(data.Slice(offset, serverPublicKey.Length));
            offset += serverPublicKey.Length;

            serverIdentity.CopyTo(data.Slice(offset, serverIdentity.Length));

            byte[] mac = CreateMac(authKey, data);

            byte[] envelope = new byte[nonce.Length + mac.Length];
            nonce.CopyTo(envelope.AsSpan()[..nonce.Length]);
            mac.CopyTo(envelope.AsSpan()[nonce.Length..]);

            return Result<byte[], OpaqueFailure>.Ok(envelope);
        }
        catch (Exception ex)
        {
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.EnvelopeFailed($"{ErrorMessages.MacEnvelopeCreationFailed}{ex.Message}", ex));
        }
    }

    public static Result<bool, OpaqueFailure> VerifyEnvelopeMac(
        ReadOnlySpan<byte> authKey,
        ReadOnlySpan<byte> envelope,
        ReadOnlySpan<byte> clientPublicKey,
        ReadOnlySpan<byte> serverPublicKey,
        ReadOnlySpan<byte> serverIdentity)
    {
        try
        {
            if (envelope.Length < NonceLength + HashLength)
                return Result<bool, OpaqueFailure>.Err(OpaqueFailure.InvalidInput(ErrorMessages.EnvelopeTooShort));

            ReadOnlySpan<byte> nonce = envelope[..NonceLength];
            ReadOnlySpan<byte> providedMac = envelope[NonceLength..];

            Result<byte[], OpaqueFailure> expectedMacResult = CreateEnvelopeMac(
                authKey, nonce, clientPublicKey, serverPublicKey, serverIdentity);

            if (expectedMacResult.IsErr)
                return Result<bool, OpaqueFailure>.Err(expectedMacResult.UnwrapErr());

            byte[] expectedEnvelope = expectedMacResult.Unwrap();
            ReadOnlySpan<byte> expectedMac = expectedEnvelope.AsSpan()[NonceLength..];

            bool isValid = CryptographicOperations.FixedTimeEquals(expectedMac, providedMac);
            return Result<bool, OpaqueFailure>.Ok(isValid);
        }
        catch (Exception ex)
        {
            return Result<bool, OpaqueFailure>.Err(OpaqueFailure.EnvelopeFailed($"{ErrorMessages.MacVerificationFailed}{ex.Message}", ex));
        }
    }

    public static Result<byte[], OpaqueFailure> UnmaskResponse(
        ReadOnlySpan<byte> maskedResponse,
        ReadOnlySpan<byte> maskingKey)
    {
        try
        {
            if (maskedResponse.Length < NonceLength)
                return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.InvalidInput(ErrorMessages.MaskedResponseTooShort));

            ReadOnlySpan<byte> nonce = maskedResponse[..NonceLength];
            ReadOnlySpan<byte> masked = maskedResponse[NonceLength..];

            byte[] pad = HkdfExpand(maskingKey.ToArray(), nonce, masked.Length);

            using ScopedSecureMemoryCollection memoryCollection = new();
            using ScopedSecureMemory unmaskBuffer = memoryCollection.Allocate(masked.Length);
            Span<byte> unmasked = unmaskBuffer.AsSpan();

            for (int i = 0; i < masked.Length; i++)
            {
                unmasked[i] = (byte)(masked[i] ^ pad[i]);
            }

            byte[] result = unmasked.ToArray();

            CryptographicOperations.ZeroMemory(pad);

            return Result<byte[], OpaqueFailure>.Ok(result);
        }
        catch (Exception ex)
        {
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.MaskingFailed($"{ErrorMessages.ResponseUnmaskingFailed}{ex.Message}", ex));
        }
    }

    public static Result<byte[], OpaqueFailure> DeriveExportKey(
        ReadOnlySpan<byte> handshakeSecret,
        ReadOnlySpan<byte> transcriptHash)
    {
        try
        {
            using ScopedSecureMemoryCollection memoryCollection = new();

            int infoLength = ExportKeyInfo.Length + transcriptHash.Length;
            using ScopedSecureMemory infoBuffer = memoryCollection.Allocate(infoLength);
            Span<byte> info = infoBuffer.AsSpan();

            ExportKeyInfo.CopyTo(info[..ExportKeyInfo.Length]);
            transcriptHash.CopyTo(info[ExportKeyInfo.Length..]);

            byte[] exportKey = HkdfExpand(handshakeSecret.ToArray(), info, DefaultKeyLength);

            return Result<byte[], OpaqueFailure>.Ok(exportKey);
        }
        catch (Exception ex)
        {
            return Result<byte[], OpaqueFailure>.Err(OpaqueFailure.KeyDerivationFailed($"{ErrorMessages.ExportKeyDerivationFailed}{ex.Message}", ex));
        }
    }
}