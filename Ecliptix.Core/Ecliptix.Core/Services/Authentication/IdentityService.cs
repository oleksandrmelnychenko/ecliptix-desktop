using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;

namespace Ecliptix.Core.Services.Authentication;

public sealed class IdentityService(ISecureProtocolStateStorage storage, IPlatformSecurityProvider platformProvider)
    : IIdentityService
{
    public async Task<bool> HasStoredIdentityAsync(string membershipId)
    {
        Result<byte[], SecureStorageFailure> result =
            await storage.LoadStateAsync($"master_{membershipId}");
        return result.IsOk;
    }

    public async Task StoreIdentityAsync(byte[] masterKey, string membershipId)
    {
        try
        {
            byte[] protectedKey = await WrapMasterKeyAsync(masterKey);
            await storage.SaveStateAsync(protectedKey, $"master_{membershipId}");

            if (platformProvider.IsHardwareSecurityAvailable())
            {
                string keychainKey = $"ecliptix_master_wrap_{membershipId}";
                byte[] wrappingKey = await GenerateWrappingKeyAsync();
                await platformProvider.StoreKeyInKeychainAsync(keychainKey, wrappingKey);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public async Task<byte[]?> LoadMasterKeyAsync(string membershipId)
    {
        try
        {
            Result<byte[], SecureStorageFailure> result =
                await storage.LoadStateAsync($"master_{membershipId}");

            if (!result.IsOk)
                return null;

            byte[] protectedKey = result.Unwrap();
            return await UnwrapMasterKeyAsync(protectedKey, membershipId);
        }
        catch
        {
            return null;
        }
    }

    private async Task<byte[]> WrapMasterKeyAsync(byte[] masterKey)
    {
        if (!platformProvider.IsHardwareSecurityAvailable())
        {
            byte[] copy = new byte[masterKey.Length];
            masterKey.CopyTo(copy, 0);
            return copy;
        }

        try
        {
            byte[] wrappingKey = await GenerateWrappingKeyAsync();

            using Aes aes = Aes.Create();
            aes.Key = wrappingKey;
            aes.GenerateIV();

            byte[] encryptedKey = aes.EncryptCbc(masterKey, aes.IV);

            byte[] wrappedData = new byte[aes.IV.Length + encryptedKey.Length];
            aes.IV.CopyTo(wrappedData, 0);
            encryptedKey.CopyTo(wrappedData, aes.IV.Length);

            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(encryptedKey);

            return wrappedData;
        }
        catch
        {
            byte[] copy = new byte[masterKey.Length];
            masterKey.CopyTo(copy, 0);
            return copy;
        }
    }

    private async Task<byte[]> UnwrapMasterKeyAsync(byte[] protectedKey, string membershipId)
    {
        if (!platformProvider.IsHardwareSecurityAvailable())
        {
            byte[] copy = new byte[protectedKey.Length];
            protectedKey.CopyTo(copy, 0);
            return copy;
        }

        try
        {
            string keychainKey = $"ecliptix_master_wrap_{membershipId}";
            byte[]? wrappingKey = await platformProvider.GetKeyFromKeychainAsync(keychainKey);

            if (wrappingKey == null)
            {
                byte[] copy = new byte[protectedKey.Length];
                protectedKey.CopyTo(copy, 0);
                return copy;
            }

            using Aes aes = Aes.Create();
            aes.Key = wrappingKey;

            byte[] iv = new byte[aes.BlockSize / 8];
            byte[] encryptedKey = new byte[protectedKey.Length - iv.Length];

            Array.Copy(protectedKey, 0, iv, 0, iv.Length);
            Array.Copy(protectedKey, iv.Length, encryptedKey, 0, encryptedKey.Length);

            aes.IV = iv;
            byte[] masterKey = aes.DecryptCbc(encryptedKey, iv);

            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(encryptedKey);

            return masterKey;
        }
        catch
        {
            byte[] copy = new byte[protectedKey.Length];
            protectedKey.CopyTo(copy, 0);
            return copy;
        }
    }

    private async Task<byte[]> GenerateWrappingKeyAsync()
    {
        return await platformProvider.GenerateSecureRandomAsync(32);
    }
}