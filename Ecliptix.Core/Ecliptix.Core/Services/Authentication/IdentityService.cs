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
    private const string MasterKeyStoragePrefix = "master_";
    private const string KeychainWrapKeyPrefix = "ecliptix_master_wrap_";
    private const int AesKeySize = 32;
    private const int AesIvSize = 16;

    private static string GetMasterKeyStorageKey(string membershipId) =>
        string.Concat(MasterKeyStoragePrefix, membershipId);

    private static string GetKeychainWrapKey(string membershipId) =>
        string.Concat(KeychainWrapKeyPrefix, membershipId);

    public async Task<bool> HasStoredIdentityAsync(string membershipId)
    {
        Result<byte[], SecureStorageFailure> result =
            await storage.LoadStateAsync(GetMasterKeyStorageKey(membershipId));
        return result.IsOk;
    }

    private async Task StoreIdentityAsync(byte[] masterKey, string membershipId)
    {
        try
        {
            byte[] protectedKey = await WrapMasterKeyAsync(masterKey);
            await storage.SaveStateAsync(protectedKey, GetMasterKeyStorageKey(membershipId));

            if (platformProvider.IsHardwareSecurityAvailable())
            {
                string keychainKey = GetKeychainWrapKey(membershipId);
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
                await storage.LoadStateAsync(GetMasterKeyStorageKey(membershipId));

            if (!result.IsOk)
                return null;

            byte[] protectedKey = result.Unwrap();
            return await UnwrapMasterKeyAsync(protectedKey, membershipId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<byte[]> WrapMasterKeyAsync(byte[] masterKey)
    {
        if (!platformProvider.IsHardwareSecurityAvailable())
        {
            return masterKey.AsSpan().ToArray();
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
        catch (Exception ex)
        {
            return masterKey.AsSpan().ToArray();
        }
    }

    private async Task<byte[]> UnwrapMasterKeyAsync(byte[] protectedKey, string membershipId)
    {
        if (!platformProvider.IsHardwareSecurityAvailable())
        {
            return protectedKey.AsSpan().ToArray();
        }

        try
        {
            string keychainKey = GetKeychainWrapKey(membershipId);
            byte[]? wrappingKey = await platformProvider.GetKeyFromKeychainAsync(keychainKey);

            if (wrappingKey == null)
            {
                return protectedKey.AsSpan().ToArray();
            }

            using Aes aes = Aes.Create();
            aes.Key = wrappingKey;

            ReadOnlySpan<byte> protectedSpan = protectedKey.AsSpan();
            byte[] iv = protectedSpan[..AesIvSize].ToArray();
            byte[] encryptedKey = protectedSpan[AesIvSize..].ToArray();

            aes.IV = iv;
            byte[] masterKey = aes.DecryptCbc(encryptedKey, iv);

            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(encryptedKey);

            return masterKey;
        }
        catch (Exception)
        {
            return protectedKey.AsSpan().ToArray();
        }
    }

    private async Task<byte[]> GenerateWrappingKeyAsync()
    {
        return await platformProvider.GenerateSecureRandomAsync(AesKeySize);
    }

    public async Task<Result<Unit, AuthenticationFailure>> ClearAllCacheAsync(string membershipId)
    {
        try
        {
            Result<Unit, SecureStorageFailure> deleteStorageResult =
                await storage.DeleteStateAsync(GetMasterKeyStorageKey(membershipId));

            if (deleteStorageResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to delete master key from storage: {deleteStorageResult.UnwrapErr().Message}"));
            }

            if (platformProvider.IsHardwareSecurityAvailable())
            {
                string keychainKey = GetKeychainWrapKey(membershipId);
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