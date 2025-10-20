using System;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Serilog;

namespace Ecliptix.Protocol.System.Core;

/// <summary>
/// Provides HKDF-based key derivation for logout authentication and revocation proof generation.
/// Implements mutual authentication protocol for secure logout operations.
/// </summary>
public static class LogoutKeyDerivation
{
    private const int KeySize = 32;
    private const int HmacSize = 32;

    // Context strings for domain separation (prevents key reuse across different purposes)
    private const string LogoutHmacKeyInfo = "ecliptix-logout-hmac-v1";
    private const string LogoutProofKeyInfo = "ecliptix-logout-proof-v1";

    // Logging tags
    private const string LogTagLogoutHmacKey = "[LOGOUT-HMAC-KEY]";
    private const string LogTagLogoutProofKey = "[LOGOUT-PROOF-KEY]";

    private const string LogMessageHmacKeyDerived = "{LogTag} Logout HMAC key derived. KeyFingerprint: {KeyFingerprint}";
    private const string LogMessageProofKeyDerived = "{LogTag} Logout proof key derived. KeyFingerprint: {KeyFingerprint}";

    private const string ErrorMessageMasterKeyReadFailed = "Failed to read master key bytes";
    private const string ErrorMessageHkdfDerivationFailed = "HKDF key derivation failed";

    /// <summary>
    /// Derives a key for HMAC-authenticating logout requests from client to server.
    /// Both client and server derive the same key from their shared session master key.
    /// </summary>
    /// <param name="masterKeyHandle">Secure handle to the session master key</param>
    /// <returns>32-byte HMAC key, or error</returns>
    public static Result<byte[], SodiumFailure> DeriveLogoutHmacKey(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        byte[]? hmacKey = null;

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(KeySize);
            if (readResult.IsErr)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ErrorMessageMasterKeyReadFailed}: {readResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = readResult.Unwrap();
            hmacKey = new byte[KeySize];

            try
            {
                HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    ikm: masterKeyBytes,
                    output: hmacKey,
                    salt: null,
                    info: Encoding.UTF8.GetBytes(LogoutHmacKeyInfo)
                );

                string keyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(hmacKey);
                Log.Debug(LogMessageHmacKeyDerived, LogTagLogoutHmacKey, keyFingerprint);

                byte[] result = hmacKey;
                hmacKey = null; // Prevent disposal in finally
                return Result<byte[], SodiumFailure>.Ok(result);
            }
            catch (CryptographicException ex)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ErrorMessageHkdfDerivationFailed}: {ex.Message}"));
            }
        }
        finally
        {
            if (masterKeyBytes != null)
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            if (hmacKey != null)
                CryptographicOperations.ZeroMemory(hmacKey);
        }
    }

    /// <summary>
    /// Derives a key for HMAC-verifying server's revocation proof.
    /// Used to verify unforgeable cryptographic proof of logout completion.
    /// </summary>
    /// <param name="masterKeyHandle">Secure handle to the session master key</param>
    /// <returns>32-byte proof verification key, or error</returns>
    public static Result<byte[], SodiumFailure> DeriveLogoutProofKey(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        byte[]? proofKey = null;

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(KeySize);
            if (readResult.IsErr)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ErrorMessageMasterKeyReadFailed}: {readResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = readResult.Unwrap();
            proofKey = new byte[KeySize];

            try
            {
                HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    ikm: masterKeyBytes,
                    output: proofKey,
                    salt: null,
                    info: Encoding.UTF8.GetBytes(LogoutProofKeyInfo)
                );

                string keyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(proofKey);
                Log.Debug(LogMessageProofKeyDerived, LogTagLogoutProofKey, keyFingerprint);

                byte[] result = proofKey;
                proofKey = null; // Prevent disposal in finally
                return Result<byte[], SodiumFailure>.Ok(result);
            }
            catch (CryptographicException ex)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ErrorMessageHkdfDerivationFailed}: {ex.Message}"));
            }
        }
        finally
        {
            if (masterKeyBytes != null)
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            if (proofKey != null)
                CryptographicOperations.ZeroMemory(proofKey);
        }
    }

    /// <summary>
    /// Computes HMAC-SHA256 over data using the provided key.
    /// Uses constant-time comparison for verification.
    /// </summary>
    /// <param name="key">32-byte HMAC key</param>
    /// <param name="data">Data to authenticate</param>
    /// <returns>32-byte HMAC tag</returns>
    public static byte[] ComputeHmac(byte[] key, byte[] data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    /// <summary>
    /// Verifies HMAC tag in constant time to prevent timing attacks.
    /// </summary>
    /// <param name="key">32-byte HMAC key</param>
    /// <param name="data">Data that was authenticated</param>
    /// <param name="expectedHmac">Expected HMAC tag to verify</param>
    /// <returns>True if HMAC is valid</returns>
    public static bool VerifyHmac(byte[] key, byte[] data, byte[] expectedHmac)
    {
        if (expectedHmac.Length != HmacSize)
            return false;

        byte[] computedHmac = ComputeHmac(key, data);
        try
        {
            return CryptographicOperations.FixedTimeEquals(computedHmac, expectedHmac);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(computedHmac);
        }
    }
}
