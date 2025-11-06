using System.Security.Cryptography;
using System.Text;
using Ecliptix.Utilities;
using Sodium;

namespace Ecliptix.Protocol.System.Security.KeyDerivation;

internal static class MasterKeyDerivation
{
    private const int KEY_SIZE = 32;
    private const int CURRENT_VERSION = 1;

    private static readonly byte[] CachedEd25519ContextBytes =
        Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.ED_25519_CONTEXT);

    private static readonly byte[] CachedX25519ContextBytes =
        Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.X_25519_CONTEXT);

    private static readonly byte[] CachedSignedPreKeyContextBytes =
        Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.SIGNED_PRE_KEY_CONTEXT);

    public static byte[] DeriveEd25519Seed(byte[] masterKey, string membershipId)
    {
        Span<byte> versionBytes = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(versionBytes, CURRENT_VERSION);

        ReadOnlySpan<byte> contextBytes = CachedEd25519ContextBytes;

        int memberBytesLength = Encoding.UTF8.GetByteCount(membershipId);
        Span<byte> memberBytes = memberBytesLength <= 256
            ? stackalloc byte[memberBytesLength]
            : new byte[memberBytesLength];
        Encoding.UTF8.GetBytes(membershipId, memberBytes);

        int totalLength = versionBytes.Length + contextBytes.Length + memberBytes.Length;
        Span<byte> combinedContext = totalLength <= 512
            ? stackalloc byte[totalLength]
            : new byte[totalLength];

        try
        {
            int offset = 0;
            versionBytes.CopyTo(combinedContext[offset..]);
            offset += versionBytes.Length;

            contextBytes.CopyTo(combinedContext[offset..]);
            offset += contextBytes.Length;

            memberBytes.CopyTo(combinedContext[offset..]);

            return HashWithGenericHashFromSpan(masterKey, combinedContext, KEY_SIZE);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(versionBytes);
            CryptographicOperations.ZeroMemory(memberBytes);
            CryptographicOperations.ZeroMemory(combinedContext);
        }
    }

    public static byte[] DeriveX25519Seed(byte[] masterKey, string membershipId)
    {
        Span<byte> versionBytes = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(versionBytes, CURRENT_VERSION);

        ReadOnlySpan<byte> contextBytes = CachedX25519ContextBytes;

        int memberBytesLength = Encoding.UTF8.GetByteCount(membershipId);
        Span<byte> memberBytes = memberBytesLength <= 256
            ? stackalloc byte[memberBytesLength]
            : new byte[memberBytesLength];
        Encoding.UTF8.GetBytes(membershipId, memberBytes);

        int totalLength = versionBytes.Length + contextBytes.Length + memberBytes.Length;
        Span<byte> combinedContext = totalLength <= 512
            ? stackalloc byte[totalLength]
            : new byte[totalLength];

        try
        {
            int offset = 0;
            versionBytes.CopyTo(combinedContext[offset..]);
            offset += versionBytes.Length;

            contextBytes.CopyTo(combinedContext[offset..]);
            offset += contextBytes.Length;

            memberBytes.CopyTo(combinedContext[offset..]);

            return HashWithGenericHashFromSpan(masterKey, combinedContext, KEY_SIZE);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(versionBytes);
            CryptographicOperations.ZeroMemory(memberBytes);
            CryptographicOperations.ZeroMemory(combinedContext);
        }
    }

    public static byte[] DeriveSignedPreKeySeed(byte[] masterKey, string membershipId)
    {
        Span<byte> versionBytes = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(versionBytes, CURRENT_VERSION);

        ReadOnlySpan<byte> contextBytes = CachedSignedPreKeyContextBytes;

        int memberBytesLength = Encoding.UTF8.GetByteCount(membershipId);
        Span<byte> memberBytes = memberBytesLength <= 256
            ? stackalloc byte[memberBytesLength]
            : new byte[memberBytesLength];
        Encoding.UTF8.GetBytes(membershipId, memberBytes);

        int totalLength = versionBytes.Length + contextBytes.Length + memberBytes.Length;
        Span<byte> combinedContext = totalLength <= 512
            ? stackalloc byte[totalLength]
            : new byte[totalLength];

        try
        {
            int offset = 0;
            versionBytes.CopyTo(combinedContext[offset..]);
            offset += versionBytes.Length;

            contextBytes.CopyTo(combinedContext[offset..]);
            offset += contextBytes.Length;

            memberBytes.CopyTo(combinedContext[offset..]);

            return HashWithGenericHashFromSpan(masterKey, combinedContext, KEY_SIZE);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(versionBytes);
            CryptographicOperations.ZeroMemory(memberBytes);
            CryptographicOperations.ZeroMemory(combinedContext);
        }
    }

    private static byte[] HashWithGenericHashFromSpan(byte[] key, ReadOnlySpan<byte> data, int outputSize) =>
        GenericHash.Hash(key, data.ToArray(), outputSize);
}
