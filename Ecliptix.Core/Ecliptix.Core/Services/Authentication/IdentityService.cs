using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Common;
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
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly NetworkProvider _networkProvider;

    public IdentityService(
        ISecureProtocolStateStorage storage,
        IPlatformSecurityProvider platformProvider,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        NetworkProvider networkProvider)
    {
        _storage = storage;
        _platformProvider = platformProvider;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _networkProvider = networkProvider;
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

            Result<Unit, AuthenticationFailure> cleanupResult = await CleanupCorruptedIdentityIfNeededAsync(context).ConfigureAwait(false);
            if (cleanupResult.IsErr)
            {
                Log.Warning("[IDENTITY-STORE] Failed to cleanup corrupted identity: {Error}", cleanupResult.UnwrapErr().Message);
            }

            await StoreIdentityInternalAsync(masterKeyHandle, context).ConfigureAwait(false);

            Result<SodiumSecureMemoryHandle, AuthenticationFailure> loadResult = await LoadMasterKeyAsync(context).ConfigureAwait(false);

            if (loadResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not load stored master key: {loadResult.UnwrapErr().Message}"));
            }

            using SodiumSecureMemoryHandle loadedKeyHandle = loadResult.Unwrap();
            Result<byte[], SodiumFailure> loadedReadResult = loadedKeyHandle.ReadBytes(loadedKeyHandle.Length);

            if (loadedReadResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Verification failed - could not read loaded master key: {loadedReadResult.UnwrapErr().Message}"));
            }

            byte[] loadedMasterKeyBytes = loadedReadResult.Unwrap();

            if (!CryptographicOperations.FixedTimeEquals(originalMasterKeyBytes, loadedMasterKeyBytes))
            {
                CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed("Master key verification failed"));
            }

            CryptographicOperations.ZeroMemory(loadedMasterKeyBytes);
            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to store/verify master key: {ex.Message}", ex));
        }
    }

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string membershipId) => await LoadMasterKeyAsync(new IdentityContext(membershipId)).ConfigureAwait(false);

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

    public async Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string membershipId, uint connectId)
    {
        Result<Unit, SecureStorageFailure> deleteResult =
            await _storage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }

        Result<Unit, AuthenticationFailure> clearResult =
            await ClearAllCacheAsync(membershipId).ConfigureAwait(false);

        if (clearResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL] Identity cache clear failed: {Error}",
                clearResult.UnwrapErr().Message);
        }

        Result<Unit, InternalServiceApiFailure> membershipClearResult =
            await _applicationSecureStorageProvider.SetApplicationMembershipAsync(null).ConfigureAwait(false);
        if (membershipClearResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL] Failed to clear membership state: {Error}",
                membershipClearResult.UnwrapErr().Message);
        }

        _networkProvider.ClearConnection(connectId);

        return Result<Unit, Exception>.Ok(Unit.Value);
    }

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
                throw new InvalidOperationException($"Failed to save master key to storage: {saveResult.UnwrapErr().Message}");
            }

            if (hardwareAvailable && wrappingKey != null)
            {
                string keychainKey = context.KeychainKey;
                await _platformProvider.StoreKeyInKeychainAsync(keychainKey, wrappingKey).ConfigureAwait(false);

                byte[]? verifyKey = await _platformProvider.GetKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);
                if (verifyKey == null)
                {
                    Log.Error("[IDENTITY-STORE-INTERNAL] CRITICAL: Wrapping key was stored but cannot be retrieved immediately! KeychainKey: {KeychainKey}", keychainKey);
                }
                else
                {
                    CryptographicOperations.ZeroMemory(verifyKey);
                }
            }
            else if (hardwareAvailable)
            {
                Log.Warning("[IDENTITY-STORE-INTERNAL] Hardware security available but no wrapping key generated (unencrypted storage)");
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
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to load protected key: {result.UnwrapErr().Message}"));
            }

            byte[] protectedKey = result.Unwrap();
            Result<SodiumSecureMemoryHandle, AuthenticationFailure> unwrapResult = await UnwrapMasterKeyAsync(protectedKey, context).ConfigureAwait(false);

            return unwrapResult;
        }
        catch (Exception ex)
        {
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
        catch (Exception ex)
        {
            Log.Error(ex, "[IDENTITY-WRAP] Failed to wrap master key with hardware security");

            if (masterKeyBytes == null)
            {
                throw;
            }

            Log.Warning("[IDENTITY-WRAP] Falling back to unencrypted storage for master key");
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
            UnwrapResult unwrapResult = hardwareSecurityAvailable
                ? await UnwrapWithHardwareSecurityAsync(protectedKey, context).ConfigureAwait(false)
                : UnwrapWithoutHardwareSecurity(protectedKey);

            if (unwrapResult.Result.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(unwrapResult.Result.UnwrapErr());
            }

            masterKeyBytes = unwrapResult.Result.Unwrap();
            wrappingKey = unwrapResult.WrappingKey;
            encryptedKey = unwrapResult.EncryptedKey;
            iv = unwrapResult.Iv;

            return WriteToSecureMemory(masterKeyBytes);
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to unwrap master key: {ex.Message}", ex));
        }
        finally
        {
            CleanupSensitiveData(masterKeyBytes, wrappingKey, encryptedKey, iv);
        }
    }

    private static UnwrapResult UnwrapWithoutHardwareSecurity(byte[] protectedKey)
    {
        return new UnwrapResult(
            Result<byte[], AuthenticationFailure>.Ok(protectedKey.AsSpan().ToArray()),
            null,
            null,
            null);
    }

    private async Task<UnwrapResult> UnwrapWithHardwareSecurityAsync(byte[] protectedKey, IdentityContext context)
    {
        string keychainKey = context.KeychainKey;
        Log.Debug("[IDENTITY-UNWRAP] Attempting to retrieve wrapping key from keychain: {KeychainKey}", keychainKey);
        byte[]? wrappingKey = await _platformProvider.GetKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);

        if (wrappingKey == null)
        {
            Result<byte[], AuthenticationFailure> result = HandleMissingWrappingKey(protectedKey);
            return new UnwrapResult(result, null, null, null);
        }

        return DecryptWithWrappingKey(protectedKey, wrappingKey);
    }

    private readonly record struct UnwrapResult(
        Result<byte[], AuthenticationFailure> Result,
        byte[]? WrappingKey,
        byte[]? EncryptedKey,
        byte[]? Iv);

    private static Result<byte[], AuthenticationFailure> HandleMissingWrappingKey(byte[] protectedKey)
    {
        const int expectedUnencryptedSize = SecureStorageConstants.Identity.AES_KEY_SIZE;

        if (protectedKey.Length > expectedUnencryptedSize)
        {
            return Result<byte[], AuthenticationFailure>.Err(
                AuthenticationFailure.KeychainCorrupted(
                    $"Master key is encrypted but wrapping key is missing from keychain. " +
                    $"This typically occurs when the keychain was cleared but encrypted files remain. " +
                    $"Protected key size: {protectedKey.Length} bytes. " +
                    $"Automatic re-initialization required."));
        }

        return Result<byte[], AuthenticationFailure>.Ok(protectedKey.AsSpan().ToArray());
    }

    private static UnwrapResult DecryptWithWrappingKey(byte[] protectedKey, byte[] wrappingKey)
    {
        using Aes aes = Aes.Create();
        aes.Key = wrappingKey;

        ReadOnlySpan<byte> protectedSpan = protectedKey.AsSpan();

        if (protectedSpan.Length <= SecureStorageConstants.Identity.AES_IV_SIZE)
        {
            Log.Error("[IDENTITY-UNWRAP] Protected key too small to contain IV, expected > {Expected}, got {Actual}",
                SecureStorageConstants.Identity.AES_IV_SIZE, protectedSpan.Length);
            throw new InvalidOperationException($"Protected key size invalid: {protectedSpan.Length} bytes");
        }

        byte[] iv = protectedSpan[..SecureStorageConstants.Identity.AES_IV_SIZE].ToArray();
        byte[] encryptedKey = protectedSpan[SecureStorageConstants.Identity.AES_IV_SIZE..].ToArray();

        aes.IV = iv;
        byte[] masterKeyBytes = aes.DecryptCbc(encryptedKey, iv);

        return new UnwrapResult(
            Result<byte[], AuthenticationFailure>.Ok(masterKeyBytes),
            wrappingKey,
            encryptedKey,
            iv);
    }

    private static Result<SodiumSecureMemoryHandle, AuthenticationFailure> WriteToSecureMemory(byte[] masterKeyBytes)
    {
        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
        if (allocResult.IsErr)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.SECURE_MEMORY_ALLOCATION_FAILED($"Failed to allocate secure memory: {allocResult.UnwrapErr().Message}"));
        }

        SodiumSecureMemoryHandle handle = allocResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = handle.Write(masterKeyBytes);
        if (writeResult.IsErr)
        {
            handle.Dispose();
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.SECURE_MEMORY_WRITE_FAILED($"Failed to write to secure memory: {writeResult.UnwrapErr().Message}"));
        }

        return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(handle);
    }

    private static void CleanupSensitiveData(byte[]? masterKeyBytes, byte[]? wrappingKey, byte[]? encryptedKey, byte[]? iv)
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

    private async Task<byte[]> GenerateWrappingKeyAsync() => await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Identity.AES_KEY_SIZE).ConfigureAwait(false);

    private bool IsHardwareSecurityAvailable() => _hardwareSecurityAvailable.Value;

    private async Task<Result<Unit, AuthenticationFailure>> CleanupCorruptedIdentityIfNeededAsync(IdentityContext context)
    {
        try
        {
            Result<byte[], SecureStorageFailure> loadResult =
                await _storage.LoadStateAsync(context.StorageKey, context.MembershipBytes).ConfigureAwait(false);

            if (loadResult.IsErr)
            {
                return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
            }

            byte[] protectedKey = loadResult.Unwrap();
            int expectedUnencryptedSize = SecureStorageConstants.Identity.AES_KEY_SIZE;
            bool hardwareSecurityAvailable = IsHardwareSecurityAvailable();

            if (hardwareSecurityAvailable && protectedKey.Length > expectedUnencryptedSize)
            {
                byte[]? wrappingKey = await _platformProvider.GetKeyFromKeychainAsync(context.KeychainKey).ConfigureAwait(false);

                if (wrappingKey == null)
                {
                    Log.Warning("[IDENTITY-CLEANUP] Detected corrupted identity - encrypted data without wrapping key. Cleaning up...");

                    Result<Unit, SecureStorageFailure> deleteStorageResult =
                        await _storage.DeleteStateAsync(context.StorageKey).ConfigureAwait(false);

                    if (deleteStorageResult.IsErr)
                    {
                        Log.Error("[IDENTITY-CLEANUP] Failed to delete corrupted storage: {Error}",
                            deleteStorageResult.UnwrapErr().Message);
                    }
                    else
                    {
                        Log.Information("[IDENTITY-CLEANUP] Successfully cleaned up corrupted identity storage");
                    }
                }
                else
                {
                    CryptographicOperations.ZeroMemory(wrappingKey);
                }
            }

            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[IDENTITY-CLEANUP] Exception during corrupted identity cleanup");
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to cleanup corrupted identity: {ex.Message}", ex));
        }
    }

    private sealed class IdentityContext(string membershipId)
    {
        private byte[]? _membershipBytes;
        public string MembershipId { get; } = membershipId;
        public string StorageKey { get; } = GetMasterKeyStorageKey(membershipId);
        public string KeychainKey { get; } = GetKeychainWrapKey(membershipId);
        public byte[] MembershipBytes => _membershipBytes ??= Guid.Parse(MembershipId).ToByteArray();

        private static string GetMasterKeyStorageKey(string membershipId) =>
            string.Concat(SecureStorageConstants.Identity.MASTER_KEY_STORAGE_PREFIX, membershipId);

        private static string GetKeychainWrapKey(string membershipId) =>
            string.Concat(SecureStorageConstants.Identity.KEYCHAIN_WRAP_KEY_PREFIX, membershipId);
    }
}
