using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;

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

    private async Task StoreIdentityAsync(byte[] masterKey, string membershipId)
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

    private async Task<byte[]?> LoadMasterKeyAsync(string membershipId)
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

    public async Task<Result<Unit, AuthenticationFailure>> ClearAllCacheAsync(string membershipId)
    {
        try
        {
            Result<Unit, SecureStorageFailure> deleteStorageResult =
                await storage.DeleteStateAsync($"master_{membershipId}");

            if (deleteStorageResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to delete master key from storage: {deleteStorageResult.UnwrapErr().Message}"));
            }

            if (platformProvider.IsHardwareSecurityAvailable())
            {
                string keychainKey = $"ecliptix_master_wrap_{membershipId}";
                await platformProvider.DeleteKeyFromKeychainAsync(keychainKey);
            }

            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to clear identity cache: {ex.Message}", ex));
        }
    }

    public async Task StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string membershipId)
    {
        byte[]? masterKeyBytes = null;
        try
        {
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                return;
            }

            masterKeyBytes = readResult.Unwrap();
            await StoreIdentityAsync(masterKeyBytes, membershipId);
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }
        }
    }

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string membershipId)
    {
        byte[]? masterKeyBytes = await LoadMasterKeyAsync(membershipId);
        if (masterKeyBytes == null)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityNotFound(membershipId));
        }

        try
        {
            Result<SodiumSecureMemoryHandle, Ecliptix.Utilities.Failures.Sodium.SodiumFailure> allocResult = SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
            if (allocResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.SecureMemoryAllocationFailed($"Failed to allocate secure memory: {allocResult.UnwrapErr().Message}"));
            }

            SodiumSecureMemoryHandle handle = allocResult.Unwrap();
            Result<Unit, Ecliptix.Utilities.Failures.Sodium.SodiumFailure> writeResult = handle.Write(masterKeyBytes);
            if (writeResult.IsErr)
            {
                handle.Dispose();
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.SecureMemoryWriteFailed($"Failed to write to secure memory: {writeResult.UnwrapErr().Message}"));
            }

            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(handle);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKeyBytes);
        }
    }
}