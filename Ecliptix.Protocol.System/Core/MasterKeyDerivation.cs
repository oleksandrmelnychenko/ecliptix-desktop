using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Konscious.Security.Cryptography;
using Sodium;

namespace Ecliptix.Protocol.System.Core;

internal static class MasterKeyDerivation
{
    private const int KEY_SIZE = 32;
    private const int CURRENT_VERSION = 1;

    private static readonly byte[] CachedDomainBytes = Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.DOMAIN_CONTEXT);
    private static readonly byte[] CachedMasterSaltBytes = Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.MASTER_SALT);
    private static readonly byte[] CachedEd25519ContextBytes = Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.ED_25519_CONTEXT);
    private static readonly byte[] CachedX25519ContextBytes = Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.X_25519_CONTEXT);
    private static readonly byte[] CachedSignedPreKeyContextBytes = Encoding.UTF8.GetBytes(StorageKeyConstants.SessionContext.SIGNED_PRE_KEY_CONTEXT);

    public static byte[] DeriveMasterKey(byte[] exportKey, ByteString membershipId)
    {
        Span<byte> membershipBytes = membershipId.Length <= 256
            ? stackalloc byte[membershipId.Length]
            : new byte[membershipId.Length];

        byte[] membershipArray = membershipId.ToByteArray();
        membershipArray.CopyTo(membershipBytes);

        Span<byte> versionBytes = stackalloc byte[sizeof(int)];
        BitConverter.TryWriteBytes(versionBytes, CURRENT_VERSION);

        ReadOnlySpan<byte> domainBytes = CachedDomainBytes;

        byte[] argonSalt = CreateArgonSalt(membershipBytes, versionBytes, domainBytes);
        byte[]? stretchedKey = null;

        ReadOnlySpan<byte> masterSaltBytes = CachedMasterSaltBytes;

        try
        {
            stretchedKey = DeriveWithArgon2Id(exportKey, argonSalt);

            byte[] salt16 = masterSaltBytes.ToArray();
            byte[] personal16 = membershipBytes.ToArray();

            if (salt16.Length != CryptographicConstants.BLAKE_2_B_SALT_SIZE)
            {
                byte[] adjustedSalt = new byte[CryptographicConstants.BLAKE_2_B_SALT_SIZE];
                int copyLength = Math.Min(salt16.Length, CryptographicConstants.BLAKE_2_B_SALT_SIZE);
                Array.Copy(salt16, 0, adjustedSalt, 0, copyLength);
                salt16 = adjustedSalt;
            }

            if (personal16.Length != CryptographicConstants.BLAKE_2_B_PERSONAL_SIZE)
            {
                throw new InvalidOperationException(string.Format(ProtocolSystemConstants.ErrorMessages.PERSONAL_PARAMETER_INVALID_SIZE, CryptographicConstants.BLAKE_2_B_PERSONAL_SIZE, personal16.Length));
            }

            byte[] masterKey = GenericHash.HashSaltPersonal(
                message: stretchedKey,
                key: null,
                salt: salt16,
                personal: personal16,
                bytes: KEY_SIZE
            );

            return masterKey;
        }
        finally
        {
            if (stretchedKey != null)
            {
                CryptographicOperations.ZeroMemory(stretchedKey);
            }

            CryptographicOperations.ZeroMemory(argonSalt);
            CryptographicOperations.ZeroMemory(membershipArray);
            CryptographicOperations.ZeroMemory(membershipBytes);
            CryptographicOperations.ZeroMemory(versionBytes);
        }
    }

    public static Result<SodiumSecureMemoryHandle, SodiumFailure> DeriveMasterKeyHandle(SodiumSecureMemoryHandle exportKeyHandle, ByteString membershipId)
    {
        return exportKeyHandle.WithReadAccess(exportKeySpan =>
        {
            Span<byte> membershipBytes = membershipId.Length <= 256
                ? stackalloc byte[membershipId.Length]
                : new byte[membershipId.Length];

            byte[] membershipArray = membershipId.ToByteArray();
            membershipArray.CopyTo(membershipBytes);

            Span<byte> versionBytes = stackalloc byte[sizeof(int)];
            BitConverter.TryWriteBytes(versionBytes, CURRENT_VERSION);

            ReadOnlySpan<byte> domainBytes = CachedDomainBytes;

            byte[] argonSalt = CreateArgonSalt(membershipBytes, versionBytes, domainBytes);
            byte[]? stretchedKey = null;

            ReadOnlySpan<byte> masterSaltBytes = CachedMasterSaltBytes;

            try
            {
                stretchedKey = DeriveWithArgon2Id(exportKeySpan, argonSalt);

                byte[] salt16 = masterSaltBytes.ToArray();
                byte[] personal16 = membershipBytes.ToArray();

                if (salt16.Length != CryptographicConstants.BLAKE_2_B_SALT_SIZE)
                {
                    byte[] adjustedSalt = new byte[CryptographicConstants.BLAKE_2_B_SALT_SIZE];
                    int copyLength = Math.Min(salt16.Length, CryptographicConstants.BLAKE_2_B_SALT_SIZE);
                    Array.Copy(salt16, 0, adjustedSalt, 0, copyLength);
                    salt16 = adjustedSalt;
                }

                if (personal16.Length != CryptographicConstants.BLAKE_2_B_PERSONAL_SIZE)
                {
                    return Result<SodiumSecureMemoryHandle, SodiumFailure>.Err(
                        SodiumFailure.InvalidOperation(string.Format(ProtocolSystemConstants.ErrorMessages.PERSONAL_PARAMETER_INVALID_SIZE, CryptographicConstants.BLAKE_2_B_PERSONAL_SIZE, personal16.Length)));
                }

                byte[] masterKeyBytes = GenericHash.HashSaltPersonal(
                    message: stretchedKey,
                    key: null,
                    salt: salt16,
                    personal: personal16,
                    bytes: KEY_SIZE
                );

                Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult = SodiumSecureMemoryHandle.Allocate(KEY_SIZE);
                if (allocResult.IsErr)
                {
                    CryptographicOperations.ZeroMemory(masterKeyBytes);
                    return allocResult;
                }

                SodiumSecureMemoryHandle masterKeyHandle = allocResult.Unwrap();
                Result<Unit, SodiumFailure> writeResult = masterKeyHandle.Write(masterKeyBytes);

                CryptographicOperations.ZeroMemory(masterKeyBytes);

                if (writeResult.IsErr)
                {
                    masterKeyHandle.Dispose();
                    return Result<SodiumSecureMemoryHandle, SodiumFailure>.Err(writeResult.UnwrapErr());
                }

                return Result<SodiumSecureMemoryHandle, SodiumFailure>.Ok(masterKeyHandle);
            }
            catch (Exception ex)
            {
                return Result<SodiumSecureMemoryHandle, SodiumFailure>.Err(
                    SodiumFailure.InvalidOperation(string.Format(ProtocolSystemConstants.ErrorMessages.FAILED_TO_DERIVE_MASTER_KEY, ex.Message)));
            }
            finally
            {
                if (stretchedKey != null)
                {
                    CryptographicOperations.ZeroMemory(stretchedKey);
                }

                CryptographicOperations.ZeroMemory(argonSalt);
                CryptographicOperations.ZeroMemory(membershipArray);
                CryptographicOperations.ZeroMemory(membershipBytes);
                CryptographicOperations.ZeroMemory(versionBytes);
            }
        });
    }

    private static byte[] CreateArgonSalt(ReadOnlySpan<byte> membershipBytes, ReadOnlySpan<byte> versionBytes, ReadOnlySpan<byte> domainBytes)
    {
        int totalLength = membershipBytes.Length + versionBytes.Length + domainBytes.Length;

        Span<byte> combinedInput = totalLength <= 512
            ? stackalloc byte[totalLength]
            : new byte[totalLength];

        try
        {
            int offset = 0;
            membershipBytes.CopyTo(combinedInput[offset..]);
            offset += membershipBytes.Length;

            versionBytes.CopyTo(combinedInput[offset..]);
            offset += versionBytes.Length;

            domainBytes.CopyTo(combinedInput[offset..]);

            byte[] salt = ComputeHashFromSpan(combinedInput);

            return salt;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(combinedInput);
        }
    }

    private static byte[] DeriveWithArgon2Id(byte[] exportKey, byte[] salt)
    {
        using Argon2id argon2 = new(exportKey)
        {
            Salt = salt,
            DegreeOfParallelism = CryptographicConstants.Argon2.DEFAULT_PARALLELISM,
            Iterations = CryptographicConstants.Argon2.DEFAULT_ITERATIONS,
            MemorySize = CryptographicConstants.Argon2.DEFAULT_MEMORY_SIZE
        };

        return argon2.GetBytes(KEY_SIZE);
    }

    private static byte[] DeriveWithArgon2Id(ReadOnlySpan<byte> exportKeySpan, byte[] salt)
    {
        byte[] exportKeyBuffer = ArrayPool<byte>.Shared.Rent(exportKeySpan.Length);
        try
        {
            exportKeySpan.CopyTo(exportKeyBuffer.AsSpan(0, exportKeySpan.Length));

            using Argon2id argon2 = new(exportKeyBuffer.AsSpan(0, exportKeySpan.Length).ToArray())
            {
                Salt = salt,
                DegreeOfParallelism = CryptographicConstants.Argon2.DEFAULT_PARALLELISM,
                Iterations = CryptographicConstants.Argon2.DEFAULT_ITERATIONS,
                MemorySize = CryptographicConstants.Argon2.DEFAULT_MEMORY_SIZE
            };

            return argon2.GetBytes(KEY_SIZE);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(exportKeyBuffer.AsSpan(0, exportKeySpan.Length));
            ArrayPool<byte>.Shared.Return(exportKeyBuffer, clearArray: false);
        }
    }

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

    private static byte[] ComputeHashFromSpan(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    private static byte[] HashWithGenericHashFromSpan(byte[] key, ReadOnlySpan<byte> data, int outputSize)
    {
        return GenericHash.Hash(key, data.ToArray(), outputSize);
    }

}
