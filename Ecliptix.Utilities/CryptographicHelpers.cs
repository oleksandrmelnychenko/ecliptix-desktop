using System;
using System.Security.Cryptography;

namespace Ecliptix.Utilities;

public static class CryptographicHelpers
{
    public static string ComputeSha256Fingerprint(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash)[..CryptographicConstants.HashFingerprintLength];
    }

    public static string ComputeSha256Fingerprint(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[CryptographicConstants.Sha256HashSize];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash)[..CryptographicConstants.HashFingerprintLength];
    }
}
