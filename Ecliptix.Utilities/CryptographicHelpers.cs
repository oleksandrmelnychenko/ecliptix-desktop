using System.Security.Cryptography;

namespace Ecliptix.Utilities;

public static class CryptographicHelpers
{
    public static string ComputeSha256Fingerprint(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash)[..CryptographicConstants.HASH_FINGERPRINT_LENGTH];
    }
}
