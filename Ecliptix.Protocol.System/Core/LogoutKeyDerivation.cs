using System;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Serilog;

namespace Ecliptix.Protocol.System.Core;

public static class LogoutKeyDerivation
{
    private const int KEY_SIZE = 32;
    private const int HMAC_SIZE = 32;

    private const string LOGOUT_HMAC_KEY_INFO = "ecliptix-logout-hmac-v1";
    private const string LOGOUT_PROOF_KEY_INFO = "ecliptix-logout-proof-v1";

    private const string LOG_TAG_LOGOUT_HMAC_KEY = "[LOGOUT-HMAC-KEY]";
    private const string LOG_TAG_LOGOUT_PROOF_KEY = "[LOGOUT-PROOF-KEY]";

    private const string LOG_MESSAGE_HMAC_KEY_DERIVED = "{LogTag} Logout HMAC key derived. KeyFingerprint: {KeyFingerprint}";
    private const string LOG_MESSAGE_PROOF_KEY_DERIVED = "{LogTag} Logout proof key derived. KeyFingerprint: {KeyFingerprint}";

    private const string ERROR_MESSAGE_MASTER_KEY_READ_FAILED = "Failed to read master key bytes";
    private const string ERROR_MESSAGE_HKDF_DERIVATION_FAILED = "HKDF key derivation failed";

    public static Result<byte[], SodiumFailure> DeriveLogoutHmacKey(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        byte[]? hmacKey = null;

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(KEY_SIZE);
            if (readResult.IsErr)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ERROR_MESSAGE_MASTER_KEY_READ_FAILED}: {readResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = readResult.Unwrap();
            hmacKey = new byte[KEY_SIZE];

            try
            {
                HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    ikm: masterKeyBytes,
                    output: hmacKey,
                    salt: null,
                    info: Encoding.UTF8.GetBytes(LOGOUT_HMAC_KEY_INFO)
                );

                string keyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(hmacKey);
                Log.Debug(LOG_MESSAGE_HMAC_KEY_DERIVED, LOG_TAG_LOGOUT_HMAC_KEY, keyFingerprint);

                byte[] result = hmacKey;
                hmacKey = null;
                return Result<byte[], SodiumFailure>.Ok(result);
            }
            catch (CryptographicException ex)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ERROR_MESSAGE_HKDF_DERIVATION_FAILED}: {ex.Message}"));
            }
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }

            if (hmacKey != null)
            {
                CryptographicOperations.ZeroMemory(hmacKey);
            }
        }
    }

    public static Result<byte[], SodiumFailure> DeriveLogoutProofKey(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        byte[]? proofKey = null;

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(KEY_SIZE);
            if (readResult.IsErr)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ERROR_MESSAGE_MASTER_KEY_READ_FAILED}: {readResult.UnwrapErr().Message}"));
            }

            masterKeyBytes = readResult.Unwrap();
            proofKey = new byte[KEY_SIZE];

            try
            {
                HKDF.DeriveKey(
                    HashAlgorithmName.SHA256,
                    ikm: masterKeyBytes,
                    output: proofKey,
                    salt: null,
                    info: Encoding.UTF8.GetBytes(LOGOUT_PROOF_KEY_INFO)
                );

                string keyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(proofKey);
                Log.Debug(LOG_MESSAGE_PROOF_KEY_DERIVED, LOG_TAG_LOGOUT_PROOF_KEY, keyFingerprint);

                byte[] result = proofKey;
                proofKey = null;
                return Result<byte[], SodiumFailure>.Ok(result);
            }
            catch (CryptographicException ex)
            {
                return Result<byte[], SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation($"{ERROR_MESSAGE_HKDF_DERIVATION_FAILED}: {ex.Message}"));
            }
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }

            if (proofKey != null)
            {
                CryptographicOperations.ZeroMemory(proofKey);
            }
        }
    }

    public static byte[] ComputeHmac(byte[] key, byte[] data)
    {
        using HMACSHA256 hmac = new HMACSHA256(key);
        return hmac.ComputeHash(data);
    }

    public static bool VerifyHmac(byte[] key, byte[] data, byte[] expectedHmac)
    {
        if (expectedHmac.Length != HMAC_SIZE)
        {
            return false;
        }

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
