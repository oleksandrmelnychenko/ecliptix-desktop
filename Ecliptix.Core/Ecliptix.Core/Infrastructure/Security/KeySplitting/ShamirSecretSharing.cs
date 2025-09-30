using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public sealed class ShamirSecretSharing : ISecureKeySplitter
{
    private const int PRIME_256_BIT_SIZE = 32;
    // Using secp256k1 prime: 2^256 - 2^32 - 977
    // This is a well-known cryptographically secure prime used in Bitcoin/Ethereum
    private static readonly BigInteger Prime256 = BigInteger.Parse("115792089237316195423570985008687907853269984665640564039457584007908634671663");

    // Configurable share expiration (default: 30 days)
    public TimeSpan ShareExpiration { get; set; } = TimeSpan.FromDays(30);

    public async Task<Result<KeySplitResult, string>> SplitKeyAsync(byte[] key, int threshold = 3, int totalShares = 5, byte[]? hmacKey = null)
    {
        if (key == null || key.Length == 0)
            return Result<KeySplitResult, string>.Err("Key cannot be null or empty");

        if (threshold < 2 || threshold > totalShares)
            return Result<KeySplitResult, string>.Err($"Invalid threshold: {threshold}. Must be between 2 and {totalShares}");

        if (totalShares < 2 || totalShares > 255)
            return Result<KeySplitResult, string>.Err($"Invalid total shares: {totalShares}. Must be between 2 and 255");

        // Validate key length to prevent integer overflow
        if (key.Length > int.MaxValue - PRIME_256_BIT_SIZE)
            return Result<KeySplitResult, string>.Err("Key is too large to process safely");

        return await Task.Run(() =>
        {
            KeyShare[] shares = null;
            byte[][] allSharesData = null;
            BigInteger[] coefficients = null;

            try
            {
                shares = new KeyShare[totalShares];
                int chunksNeeded = checked((key.Length + PRIME_256_BIT_SIZE - 1) / PRIME_256_BIT_SIZE);

                // Limit chunks to prevent DoS
                if (chunksNeeded > 1000)
                    return Result<KeySplitResult, string>.Err("Key requires too many chunks (>1000)");

                allSharesData = new byte[totalShares][];
                for (int i = 0; i < totalShares; i++)
                {
                    allSharesData[i] = new byte[chunksNeeded * (PRIME_256_BIT_SIZE + 1) + 4];
                    BitConverter.GetBytes(key.Length).CopyTo(allSharesData[i], 0);
                }

                for (int chunkIndex = 0; chunkIndex < chunksNeeded; chunkIndex++)
                {
                    int startIdx = chunkIndex * PRIME_256_BIT_SIZE;
                    int chunkSize = Math.Min(PRIME_256_BIT_SIZE, key.Length - startIdx);

                    byte[] chunk = new byte[PRIME_256_BIT_SIZE];
                    Array.Copy(key, startIdx, chunk, 0, chunkSize);

                    BigInteger secret = new BigInteger(chunk, true, true);

                    // CRITICAL FIX: Reduce secret modulo prime to ensure it's in the field
                    secret = secret % Prime256;
                    if (secret < 0) secret += Prime256;

                    coefficients = new BigInteger[threshold];
                    coefficients[0] = secret;

                    for (int i = 1; i < threshold; i++)
                    {
                        byte[] randomBytes = RandomNumberGenerator.GetBytes(PRIME_256_BIT_SIZE);
                        BigInteger coeff = new BigInteger(randomBytes, true, true) % Prime256;
                        if (coeff < 0) coeff += Prime256;
                        coefficients[i] = coeff;
                    }

                    for (int x = 1; x <= totalShares; x++)
                    {
                        BigInteger shareValue = EvaluatePolynomial(coefficients, x);

                        byte[] shareBytes = shareValue.ToByteArray(true, true);

                        // FIX: Handle both undersized and oversized byte arrays
                        if (shareBytes.Length != PRIME_256_BIT_SIZE)
                        {
                            byte[] adjustedBytes = new byte[PRIME_256_BIT_SIZE];
                            if (shareBytes.Length > PRIME_256_BIT_SIZE)
                            {
                                // Take the least significant bytes if oversized
                                Array.Copy(shareBytes, shareBytes.Length - PRIME_256_BIT_SIZE,
                                          adjustedBytes, 0, PRIME_256_BIT_SIZE);
                            }
                            else
                            {
                                // Pad with zeros at the beginning if undersized
                                Array.Copy(shareBytes, 0, adjustedBytes,
                                          PRIME_256_BIT_SIZE - shareBytes.Length, shareBytes.Length);
                            }
                            shareBytes = adjustedBytes;
                        }

                        int shareOffset = 4 + chunkIndex * (PRIME_256_BIT_SIZE + 1);
                        allSharesData[x - 1][shareOffset] = (byte)x;
                        Array.Copy(shareBytes, 0, allSharesData[x - 1], shareOffset + 1, PRIME_256_BIT_SIZE);
                    }
                }

                // Create KeySplitResult first to get the SessionId
                KeySplitResult result = new(null!, threshold); // We'll set shares after
                Guid sessionId = result.SessionId;

                // Create shares with the SessionId from KeySplitResult
                for (int i = 0; i < totalShares; i++)
                {
                    ShareLocation location = (ShareLocation)(i % Enum.GetValues<ShareLocation>().Length);
                    shares[i] = new KeyShare(allSharesData[i], i + 1, location, sessionId);

                    // Add HMAC if key provided
                    if (hmacKey != null && hmacKey.Length > 0)
                    {
                        using HMACSHA256 hmac = new(hmacKey);
                        byte[] shareHmac = hmac.ComputeHash(allSharesData[i]);
                        shares[i].SetHmac(shareHmac);
                    }
                }

                // Now set the shares in the result
                result.SetShares(shares);

                Log.Debug("Split key into {TotalShares} shares with threshold {Threshold}", totalShares, threshold);
                return Result<KeySplitResult, string>.Ok(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to split key using Shamir's Secret Sharing");

                // Dispose any created shares on error
                if (shares != null)
                {
                    foreach (KeyShare share in shares)
                    {
                        share?.Dispose();
                    }
                }

                return Result<KeySplitResult, string>.Err($"Key splitting failed: {ex.Message}");
            }
            finally
            {
                // Clear sensitive data from memory
                if (coefficients != null)
                {
                    for (int i = 0; i < coefficients.Length; i++)
                    {
                        coefficients[i] = BigInteger.Zero;
                    }
                }

                if (allSharesData != null && shares == null) // Only clear if not used in shares
                {
                    foreach (byte[] data in allSharesData)
                    {
                        if (data != null)
                            CryptographicOperations.ZeroMemory(data);
                    }
                }
            }
        });
    }

    public async Task<Result<byte[], string>> ReconstructKeyAsync(KeyShare[] shares, byte[]? hmacKey = null)
    {
        if (shares == null || shares.Length < 2)
            return Result<byte[], string>.Err("Insufficient shares for reconstruction");

        // Validate share consistency
        if (!ValidateSharesForReconstruction(shares, hmacKey, out string validationError))
            return Result<byte[], string>.Err(validationError);

        return await Task.Run(() =>
        {
            try
            {
                // Validate array bounds before reading
                if (shares[0].ShareData.Length < 4)
                    return Result<byte[], string>.Err("Invalid share format: missing length header");

                int keyLength = BitConverter.ToInt32(shares[0].ShareData, 0);

                // Validate key length is reasonable
                if (keyLength <= 0 || keyLength > 1024 * 1024) // Max 1MB
                    return Result<byte[], string>.Err($"Invalid key length: {keyLength}");

                int chunksNeeded = checked((keyLength + PRIME_256_BIT_SIZE - 1) / PRIME_256_BIT_SIZE);

                byte[] reconstructedKey = new byte[keyLength];

                for (int chunkIndex = 0; chunkIndex < chunksNeeded; chunkIndex++)
                {
                    BigInteger[] x = new BigInteger[shares.Length];
                    BigInteger[] y = new BigInteger[shares.Length];

                    for (int i = 0; i < shares.Length; i++)
                    {
                        int shareOffset = 4 + chunkIndex * (PRIME_256_BIT_SIZE + 1);

                        // Validate bounds before accessing
                        if (shareOffset >= shares[i].ShareData.Length)
                            return Result<byte[], string>.Err($"Invalid share format at index {i}, chunk {chunkIndex}");

                        if (shareOffset + 1 + PRIME_256_BIT_SIZE > shares[i].ShareData.Length)
                            return Result<byte[], string>.Err($"Share data truncated at index {i}, chunk {chunkIndex}");

                        x[i] = shares[i].ShareData[shareOffset];

                        byte[] yBytes = new byte[PRIME_256_BIT_SIZE];
                        Array.Copy(shares[i].ShareData, shareOffset + 1, yBytes, 0, PRIME_256_BIT_SIZE);
                        y[i] = new BigInteger(yBytes, true, true);
                    }

                    BigInteger reconstructedSecret = LagrangeInterpolation(x, y, 0);

                    byte[] secretBytes = reconstructedSecret.ToByteArray(true, true);
                    int startIdx = chunkIndex * PRIME_256_BIT_SIZE;
                    int copySize = Math.Min(secretBytes.Length, Math.Min(PRIME_256_BIT_SIZE, keyLength - startIdx));
                    Array.Copy(secretBytes, 0, reconstructedKey, startIdx, copySize);
                }

                Log.Debug("Successfully reconstructed key from {ShareCount} shares", shares.Length);
                return Result<byte[], string>.Ok(reconstructedKey);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to reconstruct key from shares");
                return Result<byte[], string>.Err($"Key reconstruction failed: {ex.Message}");
            }
        });
    }

    public bool ValidateShares(KeyShare[] shares, byte[]? hmacKey = null)
    {
        if (shares == null || shares.Length < 2)
            return false;

        try
        {
            // Basic validation
            int expectedLength = shares[0].ShareData.Length;
            if (!shares.All(s => s.ShareData != null && s.ShareData.Length == expectedLength))
                return false;

            // Check for duplicate share indices
            HashSet<int> seenIndices = new();
            foreach (KeyShare share in shares)
            {
                if (!seenIndices.Add(share.ShareIndex))
                    return false; // Duplicate index found
            }

            // All shares should have same SessionId
            Guid? sessionId = shares[0].SessionId;
            if (!shares.All(s => s.SessionId == sessionId))
                return false;

            // Check share indices are in valid range (1-based)
            if (shares.Any(s => s.ShareIndex < 1 || s.ShareIndex > 255))
                return false;

            // Validate HMAC if key provided
            if (hmacKey != null && hmacKey.Length > 0)
            {
                using HMACSHA256 hmac = new(hmacKey);
                foreach (KeyShare share in shares)
                {
                    if (share.Hmac == null || share.Hmac.Length == 0)
                        return false; // HMAC expected but not present

                    byte[] expectedHmac = hmac.ComputeHash(share.ShareData);
                    if (!ConstantTimeEquals(expectedHmac, share.Hmac))
                        return false; // HMAC mismatch
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool ValidateSharesForReconstruction(KeyShare[] shares, byte[]? hmacKey, out string error)
    {
        error = string.Empty;

        if (!ValidateShares(shares, hmacKey))
        {
            error = "Share validation failed";
            return false;
        }

        // Additional reconstruction-specific validation
        // Note: We allow shares from the same location type for flexibility.
        // In production, you may want to enforce location diversity based on your threat model.
        // For example, if an attacker compromises one storage location, they shouldn't
        // be able to reconstruct the key. However, during recovery scenarios or testing,
        // you might need to use shares from fewer location types.

        // Optional: Uncomment to enforce location diversity
        /*
        HashSet<ShareLocation> locations = new();
        foreach (KeyShare share in shares)
        {
            locations.Add(share.Location);
        }

        if (locations.Count < Math.Min(shares.Length, 3)) // Require at least 3 different locations if possible
        {
            error = $"Insufficient location diversity. Found {locations.Count} unique locations, recommend at least 3";
            return false;
        }
        */

        // Check share age if expiration is configured
        if (ShareExpiration > TimeSpan.Zero)
        {
            DateTime now = DateTime.UtcNow;
            foreach (KeyShare share in shares)
            {
                if (now - share.CreatedAt > ShareExpiration)
                {
                    error = $"Share {share.ShareIndex} has expired (created {share.CreatedAt:yyyy-MM-dd})";
                    return false;
                }
            }
        }

        return true;
    }

    public async Task<Result<Unit, string>> SecurelyDisposeSharesAsync(KeyShare[] shares)
    {
        return await Task.Run(() =>
        {
            try
            {
                foreach (KeyShare share in shares ?? Array.Empty<KeyShare>())
                {
                    share?.Dispose();
                }
                return Result<Unit, string>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, string>.Err($"Failed to dispose shares: {ex.Message}");
            }
        });
    }

    private static BigInteger EvaluatePolynomial(BigInteger[] coefficients, int x)
    {
        BigInteger result = 0;
        BigInteger xPower = 1;

        foreach (BigInteger coeff in coefficients)
        {
            result = (result + (coeff * xPower)) % Prime256;
            xPower = (xPower * x) % Prime256;
        }

        while (result < 0)
            result += Prime256;

        return result;
    }

    private static BigInteger LagrangeInterpolation(BigInteger[] x, BigInteger[] y, int targetX)
    {
        BigInteger result = 0;

        for (int i = 0; i < x.Length; i++)
        {
            BigInteger numerator = 1;
            BigInteger denominator = 1;

            for (int j = 0; j < x.Length; j++)
            {
                if (i != j)
                {
                    numerator = (numerator * (targetX - x[j])) % Prime256;
                    denominator = (denominator * (x[i] - x[j])) % Prime256;
                }
            }

            while (denominator < 0)
                denominator += Prime256;

            BigInteger inverseDenominator = ModInverse(denominator, Prime256);
            BigInteger term = (y[i] * numerator * inverseDenominator) % Prime256;

            result = (result + term) % Prime256;
        }

        while (result < 0)
            result += Prime256;

        return result;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        // Validate inputs
        if (m <= 1)
            throw new ArgumentException("Modulus must be greater than 1", nameof(m));

        // Normalize a to be positive and within modulus
        a = a % m;
        if (a < 0) a += m;

        // Check if inverse exists (gcd must be 1)
        BigInteger gcd = BigInteger.GreatestCommonDivisor(a, m);
        if (gcd != 1)
            throw new InvalidOperationException($"No modular inverse exists for {a} mod {m} (gcd={gcd})");

        // Extended Euclidean Algorithm
        BigInteger m0 = m;
        BigInteger x0 = 0;
        BigInteger x1 = 1;

        while (a > 1)
        {
            BigInteger q = a / m;
            BigInteger t = m;

            m = a % m;
            a = t;
            t = x0;

            x0 = x1 - q * x0;
            x1 = t;
        }

        if (x1 < 0) x1 += m0;

        return x1;
    }

    private static bool ConstantTimeEquals(byte[] a, byte[] b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;

        uint diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= (uint)(a[i] ^ b[i]);
        }
        return diff == 0;
    }
}