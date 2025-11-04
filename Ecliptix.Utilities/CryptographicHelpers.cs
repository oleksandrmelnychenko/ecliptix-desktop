using System;
using System.Security.Cryptography;

namespace Ecliptix.Utilities;

public static class CryptographicHelpers
{
    public static string ComputeSha256Fingerprint(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash)[..CryptographicConstants.HASH_FINGERPRINT_LENGTH];
    }

    public static string ComputeSha256Fingerprint(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[CryptographicConstants.SHA_256_HASH_SIZE];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash)[..CryptographicConstants.HASH_FINGERPRINT_LENGTH];
    }
}
