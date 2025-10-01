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

    private async Task StoreIdentityInternalAsync(SodiumSecureMemoryHandle masterKeyHandle, string membershipId)
    {
        byte[]? wrappingKey = null;
        try
        {
            byte[] protectedKey = await WrapMasterKeyAsync(masterKeyHandle);
            await storage.SaveStateAsync(protectedKey, GetMasterKeyStorageKey(membershipId));

            if (platformProvider.IsHardwareSecurityAvailable())
            {
                string keychainKey = GetKeychainWrapKey(membershipId);
                wrappingKey = await GenerateWrappingKeyAsync();
                await platformProvider.StoreKeyInKeychainAsync(keychainKey, wrappingKey);
            }
        }
        finally
        {
            if (wrappingKey != null)
                CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    private async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyAsync(string membershipId)
    {
        try
        {
            Result<byte[], SecureStorageFailure> result =
                await storage.LoadStateAsync(GetMasterKeyStorageKey(membershipId));

            if (result.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to load protected key: {result.UnwrapErr().Message}"));
            }

            byte[] protectedKey = result.Unwrap();
            return await UnwrapMasterKeyAsync(protectedKey, membershipId);
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to load master key: {ex.Message}", ex));
        }
    }

    private async Task<byte[]> WrapMasterKeyAsync(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        try
        {
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult =
                masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                throw new InvalidOperationException($"Failed to read master key: {readResult.UnwrapErr().Message}");
            }

            masterKeyBytes = readResult.Unwrap();

            if (!platformProvider.IsHardwareSecurityAvailable())
            {
                return masterKeyBytes.AsSpan().ToArray();
            }

            byte[]? wrappingKey = null;
            byte[]? encryptedKey = null;
            try
            {
                wrappingKey = await GenerateWrappingKeyAsync();

                using Aes aes = Aes.Create();
                aes.Key = wrappingKey;
                aes.GenerateIV();

                encryptedKey = aes.EncryptCbc(masterKeyBytes, aes.IV);

                byte[] wrappedData = new byte[aes.IV.Length + encryptedKey.Length];
                aes.IV.CopyTo(wrappedData, 0);
                encryptedKey.CopyTo(wrappedData, aes.IV.Length);

                return wrappedData;
            }
            finally
            {
                if (wrappingKey != null)
                    CryptographicOperations.ZeroMemory(wrappingKey);
                if (encryptedKey != null)
                    CryptographicOperations.ZeroMemory(encryptedKey);
            }
        }
        catch (Exception)
        {
            if (masterKeyBytes == null)
                throw;
            return masterKeyBytes.AsSpan().ToArray();
        }
        finally
        {
            if (masterKeyBytes != null)
                CryptographicOperations.ZeroMemory(masterKeyBytes);
        }
    }

    private async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> UnwrapMasterKeyAsync(byte[] protectedKey, string membershipId)
    {
        byte[]? masterKeyBytes = null;
        byte[]? wrappingKey = null;
        byte[]? encryptedKey = null;
        byte[]? iv = null;

        try
        {
            if (!platformProvider.IsHardwareSecurityAvailable())
            {
                masterKeyBytes = protectedKey.AsSpan().ToArray();
            }
            else
            {
                string keychainKey = GetKeychainWrapKey(membershipId);
                wrappingKey = await platformProvider.GetKeyFromKeychainAsync(keychainKey);

                if (wrappingKey == null)
                {
                    masterKeyBytes = protectedKey.AsSpan().ToArray();
                }
                else
                {
                    using Aes aes = Aes.Create();
                    aes.Key = wrappingKey;

                    ReadOnlySpan<byte> protectedSpan = protectedKey.AsSpan();
                    iv = protectedSpan[..AesIvSize].ToArray();
                    encryptedKey = protectedSpan[AesIvSize..].ToArray();

                    aes.IV = iv;
                    masterKeyBytes = aes.DecryptCbc(encryptedKey, iv);
                }
            }

            Result<SodiumSecureMemoryHandle, Ecliptix.Utilities.Failures.Sodium.SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
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
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to unwrap master key: {ex.Message}", ex));
        }
        finally
        {
            if (masterKeyBytes != null)
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            if (wrappingKey != null)
                CryptographicOperations.ZeroMemory(wrappingKey);
            if (encryptedKey != null)
                CryptographicOperations.ZeroMemory(encryptedKey);
            if (iv != null)
                CryptographicOperations.ZeroMemory(iv);
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
        await StoreIdentityInternalAsync(masterKeyHandle, membershipId);
    }

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string membershipId)
    {
        return await LoadMasterKeyAsync(membershipId);
    }
}