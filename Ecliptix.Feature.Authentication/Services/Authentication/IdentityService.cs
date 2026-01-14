using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protected.Protocol.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Sodium;
using Serilog;

namespace Ecliptix.Feature.Authentication.Services.Authentication;

public sealed class IdentityService : IIdentityService
{
    private static readonly byte[] WrappedKeyMagic =
        Encoding.ASCII.GetBytes(SecureStorageConstants.Identity.WRAPPED_KEY_MAGIC_HEADER);

    private readonly ISecureProtocolStateStorage _storage;
    private readonly IPlatformSecurityProvider _platformProvider;
    private readonly Lazy<bool> _hardwareSecurityAvailable;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;

    public IdentityService(
        ISecureProtocolStateStorage storage,
        IPlatformSecurityProvider platformProvider,
        IApplicationSecureStorageProvider applicationSecureStorageProvider)
    {
        _storage = storage;
        _platformProvider = platformProvider;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _hardwareSecurityAvailable = new Lazy<bool>(() => _platformProvider.IsHardwareSecurityAvailable());
    }

    public async Task<bool> HasStoredIdentityAsync(string accountId)
    {
        IdentityContext context = new(accountId);

        Result<byte[], SecureStorageFailure> result =
            await _storage.LoadStateAsync(context.StorageKey, context.AccountBytes).ConfigureAwait(false);
        bool exists = result.IsOk;

        return exists;
    }

    public async Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string accountId)
    {
        IdentityContext context = new(accountId);

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

    public async Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string accountId) =>
        await LoadMasterKeyAsync(new IdentityContext(accountId)).ConfigureAwait(false);

    public async Task<Result<Unit, AuthenticationFailure>> ClearAllCacheAsync(string accountId)
    {
        IdentityContext context = new(accountId);

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

    public async Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string accountId, uint connectId)
    {
        Result<Unit, SecureStorageFailure> deleteResult =
            await _storage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

        if (deleteResult.IsErr)
        {
            Log.Warning("[STATE-CLEANUP-FULL-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, ERROR: {Error}",
                connectId, deleteResult.UnwrapErr().Message);
        }

        Result<Unit, AuthenticationFailure> clearResult =
            await ClearAllCacheAsync(accountId).ConfigureAwait(false);

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
                await _storage.SaveStateAsync(protectedKey, storageKey, context.AccountBytes).ConfigureAwait(false);

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
                await _storage.LoadStateAsync(storageKey, context.AccountBytes).ConfigureAwait(false);

            if (result.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.IdentityStorageFailed($"Failed to load protected key: {result.UnwrapErr().Message}"));
            }

            byte[] protectedKey = result.Unwrap();
            Result<(SodiumSecureMemoryHandle Handle, bool NeedsUpgrade), AuthenticationFailure> unwrapResult =
                await UnwrapMasterKeyAsync(protectedKey, context).ConfigureAwait(false);

            if (unwrapResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(unwrapResult.UnwrapErr());
            }

            (SodiumSecureMemoryHandle handle, bool needsUpgrade) = unwrapResult.Unwrap();
            if (needsUpgrade)
            {
                await TryUpgradeWrappedKeyAsync(handle, context).ConfigureAwait(false);
            }

            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(handle);
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

            byte[]? ciphertext = null;
            byte[]? nonce = null;
            byte[]? tag = null;
            try
            {
                byte[] wrappingKey = await GenerateWrappingKeyAsync().ConfigureAwait(false);

                nonce = RandomNumberGenerator.GetBytes(SecureStorageConstants.Encryption.NONCE_SIZE);
                ciphertext = new byte[masterKeyBytes.Length];
                tag = new byte[SecureStorageConstants.Encryption.TAG_SIZE];

                using AesGcm aes = new(wrappingKey, SecureStorageConstants.Encryption.TAG_SIZE);
                aes.Encrypt(nonce, masterKeyBytes, ciphertext, tag);

                byte[] wrappedData = new byte[WrappedKeyMagic.Length + nonce.Length + tag.Length + ciphertext.Length];
                Buffer.BlockCopy(WrappedKeyMagic, 0, wrappedData, 0, WrappedKeyMagic.Length);
                Buffer.BlockCopy(nonce, 0, wrappedData, WrappedKeyMagic.Length, nonce.Length);
                Buffer.BlockCopy(tag, 0, wrappedData, WrappedKeyMagic.Length + nonce.Length, tag.Length);
                Buffer.BlockCopy(ciphertext, 0, wrappedData,
                    WrappedKeyMagic.Length + nonce.Length + tag.Length, ciphertext.Length);

                return (wrappedData, wrappingKey);
            }
            finally
            {
                if (ciphertext != null)
                {
                    CryptographicOperations.ZeroMemory(ciphertext);
                }

                if (nonce != null)
                {
                    CryptographicOperations.ZeroMemory(nonce);
                }

                if (tag != null)
                {
                    CryptographicOperations.ZeroMemory(tag);
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

    private async Task<Result<(SodiumSecureMemoryHandle Handle, bool NeedsUpgrade), AuthenticationFailure>>
        UnwrapMasterKeyAsync(byte[] protectedKey, IdentityContext context)
    {
        byte[]? masterKeyBytes = null;
        byte[]? wrappingKey = null;
        byte[]? encryptedKey = null;
        byte[]? iv = null;
        byte[]? tag = null;
        bool hardwareSecurityAvailable = IsHardwareSecurityAvailable();

        try
        {
            UnwrapResult unwrapResult = hardwareSecurityAvailable
                ? await UnwrapWithHardwareSecurityAsync(protectedKey, context).ConfigureAwait(false)
                : UnwrapWithoutHardwareSecurity(protectedKey);

            if (unwrapResult.Result.IsErr)
            {
                return Result<(SodiumSecureMemoryHandle, bool), AuthenticationFailure>.Err(
                    unwrapResult.Result.UnwrapErr());
            }

            masterKeyBytes = unwrapResult.Result.Unwrap();
            wrappingKey = unwrapResult.WrappingKey;
            encryptedKey = unwrapResult.EncryptedKey;
            iv = unwrapResult.Iv;
            tag = unwrapResult.Tag;

            Result<SodiumSecureMemoryHandle, AuthenticationFailure> handleResult = WriteToSecureMemory(masterKeyBytes);
            if (handleResult.IsErr)
            {
                return Result<(SodiumSecureMemoryHandle, bool), AuthenticationFailure>.Err(handleResult.UnwrapErr());
            }

            return Result<(SodiumSecureMemoryHandle, bool), AuthenticationFailure>.Ok(
                (handleResult.Unwrap(), unwrapResult.IsLegacy));
        }
        catch (Exception ex)
        {
            return Result<(SodiumSecureMemoryHandle, bool), AuthenticationFailure>.Err(
                AuthenticationFailure.IdentityStorageFailed($"Failed to unwrap master key: {ex.Message}", ex));
        }
        finally
        {
            CleanupSensitiveData(masterKeyBytes, wrappingKey, encryptedKey, iv, tag);
        }
    }

    private static UnwrapResult UnwrapWithoutHardwareSecurity(byte[] protectedKey)
    {
        return new UnwrapResult(
            Result<byte[], AuthenticationFailure>.Ok(protectedKey.AsSpan().ToArray()),
            null,
            null,
            null,
            null,
            false);
    }

    private async Task<UnwrapResult> UnwrapWithHardwareSecurityAsync(byte[] protectedKey, IdentityContext context)
    {
        string keychainKey = context.KeychainKey;
        Log.Debug("[IDENTITY-UNWRAP] Attempting to retrieve wrapping key from keychain: {KeychainKey}", keychainKey);
        byte[]? wrappingKey = await _platformProvider.GetKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);

        if (wrappingKey == null)
        {
            Result<byte[], AuthenticationFailure> result = HandleMissingWrappingKey(protectedKey);
            return new UnwrapResult(result, null, null, null, null, false);
        }

        return DecryptWithWrappingKey(protectedKey, wrappingKey);
    }

    private readonly record struct UnwrapResult(
        Result<byte[], AuthenticationFailure> Result,
        byte[]? WrappingKey,
        byte[]? EncryptedKey,
        byte[]? Iv,
        byte[]? Tag,
        bool IsLegacy);

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

    private static bool HasWrappedKeyMagic(ReadOnlySpan<byte> protectedKey) =>
        protectedKey.Length > WrappedKeyMagic.Length &&
        protectedKey[..WrappedKeyMagic.Length].SequenceEqual(WrappedKeyMagic);

    private static UnwrapResult DecryptWithWrappingKey(byte[] protectedKey, byte[] wrappingKey)
    {
        ReadOnlySpan<byte> protectedSpan = protectedKey.AsSpan();

        if (HasWrappedKeyMagic(protectedSpan))
        {
            return DecryptWithAesGcm(protectedSpan, wrappingKey);
        }

        return DecryptWithLegacyCbc(protectedSpan, wrappingKey);
    }

    private static UnwrapResult DecryptWithLegacyCbc(ReadOnlySpan<byte> protectedSpan, byte[] wrappingKey)
    {
        using Aes aes = Aes.Create();
        aes.Key = wrappingKey;

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
            iv,
            null,
            true);
    }

    private static UnwrapResult DecryptWithAesGcm(ReadOnlySpan<byte> protectedSpan, byte[] wrappingKey)
    {
        int headerSize = WrappedKeyMagic.Length;
        int nonceSize = SecureStorageConstants.Encryption.NONCE_SIZE;
        int tagSize = SecureStorageConstants.Encryption.TAG_SIZE;
        int minSize = headerSize + nonceSize + tagSize + 1;

        if (protectedSpan.Length < minSize)
        {
            Log.Error(
                "[IDENTITY-UNWRAP] Protected key too small to contain AEAD payload, expected >= {Expected}, got {Actual}",
                minSize, protectedSpan.Length);
            throw new InvalidOperationException($"Protected key size invalid: {protectedSpan.Length} bytes");
        }

        ReadOnlySpan<byte> nonce = protectedSpan.Slice(headerSize, nonceSize);
        ReadOnlySpan<byte> tag = protectedSpan.Slice(headerSize + nonceSize, tagSize);
        ReadOnlySpan<byte> ciphertext = protectedSpan.Slice(headerSize + nonceSize + tagSize);

        byte[] plaintext = new byte[ciphertext.Length];
        using AesGcm aes = new(wrappingKey, SecureStorageConstants.Encryption.TAG_SIZE);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return new UnwrapResult(
            Result<byte[], AuthenticationFailure>.Ok(plaintext),
            wrappingKey,
            ciphertext.ToArray(),
            nonce.ToArray(),
            tag.ToArray(),
            false);
    }

    private static Result<SodiumSecureMemoryHandle, AuthenticationFailure> WriteToSecureMemory(byte[] masterKeyBytes)
    {
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

    private static void CleanupSensitiveData(byte[]? masterKeyBytes, byte[]? wrappingKey, byte[]? encryptedKey,
        byte[]? iv, byte[]? tag)
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

        if (tag != null)
        {
            CryptographicOperations.ZeroMemory(tag);
        }
    }

    private async Task TryUpgradeWrappedKeyAsync(SodiumSecureMemoryHandle masterKeyHandle, IdentityContext context)
    {
        try
        {
            await StoreIdentityInternalAsync(masterKeyHandle, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[IDENTITY-UPGRADE] Failed to upgrade wrapped master key format");
        }
    }

    private async Task<byte[]> GenerateWrappingKeyAsync() => await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Identity.AES_KEY_SIZE).ConfigureAwait(false);

    private bool IsHardwareSecurityAvailable() => _hardwareSecurityAvailable.Value;

    private async Task<Result<Unit, AuthenticationFailure>> CleanupCorruptedIdentityIfNeededAsync(IdentityContext context)
    {
        try
        {
            Result<byte[], SecureStorageFailure> loadResult =
                await _storage.LoadStateAsync(context.StorageKey, context.AccountBytes).ConfigureAwait(false);

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

    private sealed class IdentityContext(string accountId)
    {
        private byte[]? _accountBytes;
        public string AccountId { get; } = accountId;
        public string StorageKey { get; } = GetMasterKeyStorageKey(accountId);
        public string KeychainKey { get; } = GetKeychainWrapKey(accountId);
        public byte[] AccountBytes => _accountBytes ??= Guid.Parse(AccountId).ToByteArray();

        private static string GetMasterKeyStorageKey(string accountId) =>
            string.Concat(SecureStorageConstants.Identity.MASTER_KEY_STORAGE_PREFIX, accountId);

        private static string GetKeychainWrapKey(string accountId) =>
            string.Concat(SecureStorageConstants.Identity.KEYCHAIN_WRAP_KEY_PREFIX, accountId);
    }
}
