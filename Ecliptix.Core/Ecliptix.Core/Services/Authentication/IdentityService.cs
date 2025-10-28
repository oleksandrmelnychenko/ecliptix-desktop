using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Sodium;
using Serilog;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class IdentityService : IIdentityService
{
    private readonly ISecureProtocolStateStorage _storage;
    private readonly IPlatformSecurityProvider _platformProvider;
    private readonly Lazy<bool> _hardwareSecurityAvailable;

    public IdentityService(ISecureProtocolStateStorage storage, IPlatformSecurityProvider platformProvider)
    {
        _storage = storage;
        _platformProvider = platformProvider;
        _hardwareSecurityAvailable = new Lazy<bool>(() => _platformProvider.IsHardwareSecurityAvailable());
    }

    public async Task<bool> HasStoredIdentityAsync(string membershipId)
    {
        IdentityContext context = new(membershipId);

        Result<byte[], SecureStorageFailure> result =
            await _storage.LoadStateAsync(context.StorageKey, context.MembershipBytes).ConfigureAwait(false);
        bool exists = result.IsOk;

        return exists;
    }

    public async Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string membershipId)
    {
        IdentityContext context = new(membershipId);

        try
        {
            Result<byte[], SodiumFailure> readResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to read master key for storage: {readResult.UnwrapErr().Message}"));
            }

            byte[] originalMasterKeyBytes = readResult.Unwrap();
            string originalFingerprint = Convert.ToHexString(SHA256.HashData(originalMasterKeyBytes))[..16];

            await StoreIdentityInternalAsync(masterKeyHandle, context).ConfigureAwait(false);

            Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult = await LoadMasterKeyAsync(context).ConfigureAwait(false);

            if (loadResult.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-VERIFY-FAIL] Failed to load master key for verification. MembershipId: {MembershipId}, Error: {Error}",
                    context.MembershipId, loadResult.UnwrapErr().Message);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not load stored master key: {loadResult.UnwrapErr().Message}"));
            }

            using SodiumSecureMemoryHandle loadedKeyHandle = loadResult.Unwrap();
            Result<byte[], SodiumFailure> loadedReadResult = loadedKeyHandle.ReadBytes(loadedKeyHandle.Length);

            if (loadedReadResult.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-VERIFY-FAIL] Failed to read loaded master key. MembershipId: {MembershipId}, Error: {Error}",
                    context.MembershipId, loadedReadResult.UnwrapErr().Message);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not read loaded master key: {loadedReadResult.UnwrapErr().Message}"));
            }

            byte[] loadedMasterKeyBytes = loadedReadResult.Unwrap();
            string loadedFingerprint = Convert.ToHexString(SHA256.HashData(loadedMasterKeyBytes))[..16];

            if (!originalMasterKeyBytes.AsSpan().SequenceEqual(loadedMasterKeyBytes))
            {
                Log.Error("[CLIENT-IDENTITY-VERIFY-MISMATCH] Master key verification failed! MembershipId: {MembershipId}, Original: {Original}, Loaded: {Loaded}",
                    context.MembershipId, originalFingerprint, loadedFingerprint);

                CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Master key verification failed - fingerprints don't match! Original: {originalFingerprint}, Loaded: {loadedFingerprint}"));
            }

            CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-IDENTITY-STORE-ERROR] Exception during master key storage/verification. MembershipId: {MembershipId}",
                context.MembershipId);
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to store/verify master key: {ex.Message}", ex));
        }
    }

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string membershipId)
    {
        return await LoadMasterKeyAsync(new IdentityContext(membershipId)).ConfigureAwait(false);
    }

    public async Task<Result<Unit, AuthenticationFailure>> ClearAllCacheAsync(string membershipId)
    {
        IdentityContext context = new(membershipId);

        try
        {
            Result<Unit, SecureStorageFailure> deleteStorageResult =
                await _storage.DeleteStateAsync(context.StorageKey).ConfigureAwait(false);

            if (deleteStorageResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to delete master key from storage: {deleteStorageResult.UnwrapErr().Message}"));
            }

            if (IsHardwareSecurityAvailable())
            {
                await _platformProvider.DeleteKeyFromKeychainAsync(context.KeychainKey).ConfigureAwait(false);
            }

            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to clear identity cache: {ex.Message}", ex));
        }
    }

    private static string GetMasterKeyStorageKey(string membershipId) =>
        string.Concat(SecureStorageConstants.Identity.MasterKeyStoragePrefix, membershipId);

    private static string GetKeychainWrapKey(string membershipId) =>
        string.Concat(SecureStorageConstants.Identity.KeychainWrapKeyPrefix, membershipId);

    private async Task StoreIdentityInternalAsync(SodiumSecureMemoryHandle masterKeyHandle, IdentityContext context)
    {
        byte[]? wrappingKey = null;
        string storageKey = context.StorageKey;
        bool hardwareAvailable = IsHardwareSecurityAvailable();

        try
        {
            (byte[] protectedKey, byte[]? returnedWrappingKey) = await WrapMasterKeyAsync(masterKeyHandle).ConfigureAwait(false);
            wrappingKey = returnedWrappingKey;
            Result<Unit, SecureStorageFailure> saveResult =
                await _storage.SaveStateAsync(protectedKey, storageKey, context.MembershipBytes).ConfigureAwait(false);

            if (saveResult.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-STORE-ERROR] Failed to save master key to storage. MembershipId: {MembershipId}, StorageKey: {StorageKey}, Error: {Error}",
                    context.MembershipId, storageKey, saveResult.UnwrapErr().Message);
                throw new InvalidOperationException($"Failed to save master key to storage: {saveResult.UnwrapErr().Message}");
            }

            if (hardwareAvailable && wrappingKey != null)
            {
                string keychainKey = context.KeychainKey;
                await _platformProvider.StoreKeyInKeychainAsync(keychainKey, wrappingKey).ConfigureAwait(false);
            }
        }
        finally
        {
            if (wrappingKey != null)
            {
                CryptographicOperations.ZeroMemory(wrappingKey);
            }
        }
    }

    private async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyAsync(IdentityContext context)
    {
        string storageKey = context.StorageKey;

        try
        {
            Result<byte[], SecureStorageFailure> result =
                await _storage.LoadStateAsync(storageKey, context.MembershipBytes).ConfigureAwait(false);

            if (result.IsErr)
            {
                Log.Error("[CLIENT-IDENTITY-LOAD-ERROR] Failed to load protected key. MembershipId: {MembershipId}, StorageKey: {StorageKey}, Error: {Error}",
                    context.MembershipId, storageKey, result.UnwrapErr().Message);

                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to load protected key: {result.UnwrapErr().Message}"));
            }

            byte[] protectedKey = result.Unwrap();
            Result<SodiumSecureMemoryHandle, AuthenticationFailure> unwrapResult = await UnwrapMasterKeyAsync(protectedKey, context).ConfigureAwait(false);

            return unwrapResult;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-IDENTITY-LOAD-EXCEPTION] Exception loading master key. MembershipId: {MembershipId}, StorageKey: {StorageKey}",
                context.MembershipId, storageKey);

            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to load master key: {ex.Message}", ex));
        }
    }

    private async Task<(byte[] wrappedData, byte[]? wrappingKey)> WrapMasterKeyAsync(SodiumSecureMemoryHandle masterKeyHandle)
    {
        byte[]? masterKeyBytes = null;
        bool hardwareSecurityAvailable = IsHardwareSecurityAvailable();

        try
        {
            Result<byte[], SodiumFailure> readResult =
                masterKeyHandle.ReadBytes(masterKeyHandle.Length);
            if (readResult.IsErr)
            {
                throw new InvalidOperationException($"Failed to read master key: {readResult.UnwrapErr().Message}");
            }

            masterKeyBytes = readResult.Unwrap();

            if (!hardwareSecurityAvailable)
            {
                return (masterKeyBytes.AsSpan().ToArray(), null);
            }

            byte[]? encryptedKey = null;
            try
            {
                byte[] wrappingKey = await GenerateWrappingKeyAsync().ConfigureAwait(false);

                using Aes aes = Aes.Create();
                aes.Key = wrappingKey;
                aes.GenerateIV();

                encryptedKey = aes.EncryptCbc(masterKeyBytes, aes.IV);

                byte[] wrappedData = new byte[aes.IV.Length + encryptedKey.Length];
                aes.IV.CopyTo(wrappedData, 0);
                encryptedKey.CopyTo(wrappedData, aes.IV.Length);

                return (wrappedData, wrappingKey);
            }
            finally
            {
                if (encryptedKey != null)
                {
                    CryptographicOperations.ZeroMemory(encryptedKey);
                }
            }
        }
        catch (Exception)
        {
            if (masterKeyBytes == null)
            {
                throw;
            }

            return (masterKeyBytes.AsSpan().ToArray(), null);
        }
        finally
        {
            if (masterKeyBytes != null)
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }
        }
    }

    private async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> UnwrapMasterKeyAsync(byte[] protectedKey, IdentityContext context)
    {
        byte[]? masterKeyBytes = null;
        byte[]? wrappingKey = null;
        byte[]? encryptedKey = null;
        byte[]? iv = null;
        bool hardwareSecurityAvailable = IsHardwareSecurityAvailable();

        try
        {
            if (!hardwareSecurityAvailable)
            {
                masterKeyBytes = protectedKey.AsSpan().ToArray();
            }
            else
            {
                string keychainKey = context.KeychainKey;
                wrappingKey = await _platformProvider.GetKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);

                if (wrappingKey == null)
                {
                    Log.Warning("[CLIENT-IDENTITY-UNWRAP] Wrapping key not found in keychain, falling back to software protection. KeychainKey: {KeychainKey}",
                        keychainKey);
                    masterKeyBytes = protectedKey.AsSpan().ToArray();
                }
                else
                {
                    using Aes aes = Aes.Create();
                    aes.Key = wrappingKey;

                    ReadOnlySpan<byte> protectedSpan = protectedKey.AsSpan();
                    iv = protectedSpan[..SecureStorageConstants.Identity.AesIvSize].ToArray();
                    encryptedKey = protectedSpan[SecureStorageConstants.Identity.AesIvSize..].ToArray();

                    aes.IV = iv;
                    masterKeyBytes = aes.DecryptCbc(encryptedKey, iv);
                }
            }

            Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
                SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
            if (allocResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.SecureMemoryAllocationFailed($"Failed to allocate secure memory: {allocResult.UnwrapErr().Message}"));
            }

            SodiumSecureMemoryHandle handle = allocResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = handle.Write(masterKeyBytes);
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
            {
                CryptographicOperations.ZeroMemory(masterKeyBytes);
            }

            if (wrappingKey != null)
            {
                CryptographicOperations.ZeroMemory(wrappingKey);
            }

            if (encryptedKey != null)
            {
                CryptographicOperations.ZeroMemory(encryptedKey);
            }

            if (iv != null)
            {
                CryptographicOperations.ZeroMemory(iv);
            }
        }
    }

    private async Task<byte[]> GenerateWrappingKeyAsync()
    {
        return await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Identity.AesKeySize).ConfigureAwait(false);
    }

    private bool IsHardwareSecurityAvailable() => _hardwareSecurityAvailable.Value;

    private sealed class IdentityContext(string membershipId)
    {
        private byte[]? _membershipBytes;

        public string MembershipId { get; } = membershipId;
        public string StorageKey { get; } = GetMasterKeyStorageKey(membershipId);
        public string KeychainKey { get; } = GetKeychainWrapKey(membershipId);

        public byte[] MembershipBytes => _membershipBytes ??= Guid.Parse(MembershipId).ToByteArray();
    }
}
