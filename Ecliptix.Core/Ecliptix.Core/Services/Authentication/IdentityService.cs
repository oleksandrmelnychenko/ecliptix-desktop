using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Serilog;

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
        string storageKey = GetMasterKeyStorageKey(membershipId);
        Result<byte[], SecureStorageFailure> result =
            await storage.LoadStateAsync(storageKey);
        bool exists = result.IsOk;

        Log.Information("[CLIENT-IDENTITY-CHECK] Checking stored identity. MembershipId: {MembershipId}, StorageKey: {StorageKey}, Exists: {Exists}",
            membershipId, storageKey, exists);

        return exists;
    }

    private async Task StoreIdentityInternalAsync(SodiumSecureMemoryHandle masterKeyHandle, string membershipId)
    {
        byte[]? wrappingKey = null;
        string storageKey = GetMasterKeyStorageKey(membershipId);

        try
        {
            Log.Information("[CLIENT-IDENTITY-STORE-START] Starting identity storage. MembershipId: {MembershipId}, StorageKey: {StorageKey}",
                membershipId, storageKey);

            (byte[] protectedKey, byte[]? returnedWrappingKey) = await WrapMasterKeyAsync(masterKeyHandle);
            wrappingKey = returnedWrappingKey;
            await storage.SaveStateAsync(protectedKey, storageKey);

            Log.Information("[CLIENT-IDENTITY-STORE] Master key stored. MembershipId: {MembershipId}, StorageKey: {StorageKey}, HardwareSecurityAvailable: {HardwareSecurityAvailable}",
                membershipId, storageKey, platformProvider.IsHardwareSecurityAvailable());

            if (platformProvider.IsHardwareSecurityAvailable() && wrappingKey != null)
            {
                string keychainKey = GetKeychainWrapKey(membershipId);
                await platformProvider.StoreKeyInKeychainAsync(keychainKey, wrappingKey);

                Log.Information("[CLIENT-IDENTITY-KEYCHAIN] Wrapping key stored in keychain. MembershipId: {MembershipId}, KeychainKey: {KeychainKey}",
                    membershipId, keychainKey);
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
        string storageKey = GetMasterKeyStorageKey(membershipId);

        try
        {
            Log.Information("[CLIENT-IDENTITY-LOAD-START] Starting master key load. MembershipId: {MembershipId}, StorageKey: {StorageKey}",
                membershipId, storageKey);

            Result<byte[], SecureStorageFailure> result =
                await storage.LoadStateAsync(storageKey);

            if (result.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-LOAD-ERROR] Failed to load protected key. MembershipId: {MembershipId}, StorageKey: {StorageKey}, Error: {Error}",
                    membershipId, storageKey, result.UnwrapErr().Message);

                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to load protected key: {result.UnwrapErr().Message}"));
            }

            byte[] protectedKey = result.Unwrap();
            Result<SodiumSecureMemoryHandle, AuthenticationFailure> unwrapResult = await UnwrapMasterKeyAsync(protectedKey, membershipId);

            if (unwrapResult.IsOk)
            {
                Log.Information("[CLIENT-IDENTITY-LOAD] Master key loaded successfully. MembershipId: {MembershipId}, StorageKey: {StorageKey}",
                    membershipId, storageKey);
            }

            return unwrapResult;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-IDENTITY-LOAD-EXCEPTION] Exception loading master key. MembershipId: {MembershipId}, StorageKey: {StorageKey}",
                membershipId, storageKey);

            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to load master key: {ex.Message}", ex));
        }
    }

    private async Task<(byte[] wrappedData, byte[]? wrappingKey)> WrapMasterKeyAsync(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        bool hardwareSecurityAvailable = platformProvider.IsHardwareSecurityAvailable();

        try
        {
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult =
                masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                throw new InvalidOperationException($"Failed to read master key: {readResult.UnwrapErr().Message}");
            }

            masterKeyBytes = readResult.Unwrap();

            Log.Information("[CLIENT-IDENTITY-WRAP] Wrapping master key. HardwareSecurityAvailable: {HardwareSecurityAvailable}",
                hardwareSecurityAvailable);

            if (!hardwareSecurityAvailable)
            {
                Log.Information("[CLIENT-IDENTITY-WRAP] Using software-only protection (no hardware security)");
                return (masterKeyBytes.AsSpan().ToArray(), null);
            }

            byte[]? wrappingKey = null;
            byte[]? encryptedKey = null;
            try
            {
                Log.Information("[CLIENT-IDENTITY-WRAP] Using hardware-backed AES encryption");

                wrappingKey = await GenerateWrappingKeyAsync();

                using Aes aes = Aes.Create();
                aes.Key = wrappingKey;
                aes.GenerateIV();

                encryptedKey = aes.EncryptCbc(masterKeyBytes, aes.IV);

                byte[] wrappedData = new byte[aes.IV.Length + encryptedKey.Length];
                aes.IV.CopyTo(wrappedData, 0);
                encryptedKey.CopyTo(wrappedData, aes.IV.Length);

                Log.Information("[CLIENT-IDENTITY-WRAP] Master key wrapped successfully with hardware encryption");

                return (wrappedData, wrappingKey);
            }
            finally
            {
                if (encryptedKey != null)
                    CryptographicOperations.ZeroMemory(encryptedKey);
            }
        }
        catch (Exception)
        {
            if (masterKeyBytes == null)
                throw;
            return (masterKeyBytes.AsSpan().ToArray(), null);
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
        bool hardwareSecurityAvailable = platformProvider.IsHardwareSecurityAvailable();

        try
        {
            Log.Information("[CLIENT-IDENTITY-UNWRAP] Unwrapping master key. MembershipId: {MembershipId}, HardwareSecurityAvailable: {HardwareSecurityAvailable}",
                membershipId, hardwareSecurityAvailable);

            if (!hardwareSecurityAvailable)
            {
                Log.Information("[CLIENT-IDENTITY-UNWRAP] Using software-only protection (no hardware security)");
                masterKeyBytes = protectedKey.AsSpan().ToArray();
            }
            else
            {
                string keychainKey = GetKeychainWrapKey(membershipId);
                wrappingKey = await platformProvider.GetKeyFromKeychainAsync(keychainKey);

                if (wrappingKey == null)
                {
                    Log.Warning("[CLIENT-IDENTITY-UNWRAP] Wrapping key not found in keychain, falling back to software protection. KeychainKey: {KeychainKey}",
                        keychainKey);
                    masterKeyBytes = protectedKey.AsSpan().ToArray();
                }
                else
                {
                    Log.Information("[CLIENT-IDENTITY-UNWRAP] Using hardware-backed AES decryption");

                    using Aes aes = Aes.Create();
                    aes.Key = wrappingKey;

                    ReadOnlySpan<byte> protectedSpan = protectedKey.AsSpan();
                    iv = protectedSpan[..AesIvSize].ToArray();
                    encryptedKey = protectedSpan[AesIvSize..].ToArray();

                    aes.IV = iv;
                    masterKeyBytes = aes.DecryptCbc(encryptedKey, iv);

                    Log.Information("[CLIENT-IDENTITY-UNWRAP] Master key unwrapped successfully with hardware decryption");
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

    public async Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string membershipId)
    {
        try
        {
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to read master key for storage: {readResult.UnwrapErr().Message}"));
            }

            byte[] originalMasterKeyBytes = readResult.Unwrap();
            string originalFingerprint = Convert.ToHexString(SHA256.HashData(originalMasterKeyBytes))[..16];

            Log.Information("[CLIENT-IDENTITY-STORE-PRE] Master key fingerprint before storage. MembershipId: {MembershipId}, Fingerprint: {Fingerprint}",
                membershipId, originalFingerprint);

            await StoreIdentityInternalAsync(masterKeyHandle, membershipId);

            Log.Information("[CLIENT-IDENTITY-VERIFY] Verifying stored master key. MembershipId: {MembershipId}",
                membershipId);

            Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult = await LoadMasterKeyAsync(membershipId);

            if (loadResult.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-VERIFY-FAIL] Failed to load master key for verification. MembershipId: {MembershipId}, Error: {Error}",
                    membershipId, loadResult.UnwrapErr().Message);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not load stored master key: {loadResult.UnwrapErr().Message}"));
            }

            using SodiumSecureMemoryHandle loadedKeyHandle = loadResult.Unwrap();
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> loadedReadResult = loadedKeyHandle.ReadBytes(loadedKeyHandle.Length);

            if (loadedReadResult.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-VERIFY-FAIL] Failed to read loaded master key. MembershipId: {MembershipId}, Error: {Error}",
                    membershipId, loadedReadResult.UnwrapErr().Message);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not read loaded master key: {loadedReadResult.UnwrapErr().Message}"));
            }

            byte[] loadedMasterKeyBytes = loadedReadResult.Unwrap();
            string loadedFingerprint = Convert.ToHexString(SHA256.HashData(loadedMasterKeyBytes))[..16];

            Log.Information("[CLIENT-IDENTITY-VERIFY-POST] Master key fingerprint after load. MembershipId: {MembershipId}, Fingerprint: {Fingerprint}",
                membershipId, loadedFingerprint);

            if (!originalMasterKeyBytes.AsSpan().SequenceEqual(loadedMasterKeyBytes))
            {
                Log.Error("[CLIENT-IDENTITY-VERIFY-MISMATCH] Master key verification failed! MembershipId: {MembershipId}, Original: {Original}, Loaded: {Loaded}",
                    membershipId, originalFingerprint, loadedFingerprint);

                CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Master key verification failed - fingerprints don't match! Original: {originalFingerprint}, Loaded: {loadedFingerprint}"));
            }

            Log.Information("[CLIENT-IDENTITY-VERIFY-SUCCESS] Master key verification passed. MembershipId: {MembershipId}, Fingerprint: {Fingerprint}",
                membershipId, originalFingerprint);

            CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-IDENTITY-STORE-ERROR] Exception during master key storage/verification. MembershipId: {MembershipId}",
                membershipId);
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to store/verify master key: {ex.Message}", ex));
        }
    }

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string membershipId)
    {
        return await LoadMasterKeyAsync(membershipId);
    }
}