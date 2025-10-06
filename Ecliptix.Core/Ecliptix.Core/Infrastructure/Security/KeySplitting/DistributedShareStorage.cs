using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Services.Common;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public sealed class DistributedShareStorage : IDistributedShareStorage, IAsyncDisposable, IDisposable
{

    private readonly IPlatformSecurityProvider _platformSecurityProvider;
    private readonly IApplicationSecureStorageProvider _secureStorageProvider;
    private readonly ISecretSharingService _keySplitter;
    private readonly ConcurrentDictionary<Guid, SodiumSecureMemoryHandle> _memoryShareCache = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _memoryCacheAccessTimes = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _keychainTracker = new();
    private readonly ReaderWriterLockSlim _cacheLock = new();
    private readonly Lock _storageLock = new();
    private readonly IHmacKeyManager? _shareAuthenticationService;
    private bool _disposed;

    public DistributedShareStorage(
        IPlatformSecurityProvider platformSecurityProvider,
        IApplicationSecureStorageProvider secureStorageProvider,
        ISecretSharingService keySplitter,
        IHmacKeyManager? shareAuthenticationService = null)
    {
        _platformSecurityProvider = platformSecurityProvider;
        _secureStorageProvider = secureStorageProvider;
        _keySplitter = keySplitter;
        _shareAuthenticationService = shareAuthenticationService;
    }

    public async Task<Result<Unit, KeySplittingFailure>> StoreKeySharesAsync(KeySplitResult splitKeys, Guid membershipId)
    {
        lock (_storageLock)
        {
            if (_disposed)
                return Result<Unit, KeySplittingFailure>.Err(KeySplittingFailure.StorageDisposed());
        }

        List<Task<Result<Unit, KeySplittingFailure>>> storageTasks = [];

        for (int i = 0; i < splitKeys.Shares.Length && i < ShareDistributionConstants.DefaultTotalShares; i++)
        {
            KeyShare share = splitKeys.Shares[i];
            storageTasks.Add(StoreShareByLocationAsync(share, membershipId, i));
        }

        Result<Unit, KeySplittingFailure>[] results = await Task.WhenAll(storageTasks);

        int successCount = results.Count(r => r.IsOk);
        if (successCount < splitKeys.Threshold)
        {
            await RemoveKeySharesAsync(membershipId);
            return Result<Unit, KeySplittingFailure>.Err(
                KeySplittingFailure.MinimumSharesNotMet(successCount, splitKeys.Threshold));
        }

        return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);
    }

    public async Task<Result<KeyShare[], KeySplittingFailure>> RetrieveKeySharesAsync(Guid membershipId,
        int minimumShares = ShareDistributionConstants.DefaultMinimumThreshold)
    {
        lock (_storageLock)
        {
            if (_disposed)
                return Result<KeyShare[], KeySplittingFailure>.Err(KeySplittingFailure.StorageDisposed());
        }

        List<Task<Result<KeyShare, KeySplittingFailure>>> retrievalTasks = new();

        for (int i = 0; i < ShareDistributionConstants.DefaultTotalShares; i++)
        {
            retrievalTasks.Add(RetrieveShareByLocationAsync(membershipId, i));
        }

        Result<KeyShare, KeySplittingFailure>[] results = await Task.WhenAll(retrievalTasks);

        List<KeyShare> retrievedShares = results
            .Where(r => r.IsOk)
            .Select(r => r.Unwrap())
            .ToList();

        if (retrievedShares.Count < minimumShares)
        {
            foreach (KeyShare share in retrievedShares)
            {
                share?.Dispose();
            }

            return Result<KeyShare[], KeySplittingFailure>.Err(
                KeySplittingFailure.InsufficientShares(retrievedShares.Count, minimumShares));
        }

        return Result<KeyShare[], KeySplittingFailure>.Ok(retrievedShares.Take(minimumShares).ToArray());
    }

    public async Task<Result<Unit, KeySplittingFailure>> RemoveKeySharesAsync(Guid membershipId)
    {
        string identifier = membershipId.ToString();

        await CleanupKeychainEntriesForIdentifierAsync(identifier);

        List<Task> removalTasks = [];

        if (_platformSecurityProvider.IsHardwareSecurityAvailable())
        {
            removalTasks.Add(_platformSecurityProvider.DeleteKeyFromKeychainAsync($"{StorageKeyConstants.Share.HardwarePrefix}{identifier}_0"));
        }

        for (int i = 1; i <= 4; i++)
        {
            removalTasks.Add(_platformSecurityProvider.DeleteKeyFromKeychainAsync($"{StorageKeyConstants.Share.KeychainPrefix}{identifier}_{i}"));
        }

        _cacheLock.EnterWriteLock();
        try
        {
            if (_memoryShareCache.TryRemove(membershipId, out SodiumSecureMemoryHandle? memHandle))
            {
                memHandle?.Dispose();
                _memoryCacheAccessTimes.TryRemove(membershipId, out _);
            }
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        _secureStorageProvider.DeleteAsync($"local_share_{identifier}");

        await Task.WhenAll(removalTasks);

        if (_shareAuthenticationService != null)
        {
            await _shareAuthenticationService.RemoveHmacKeyAsync(identifier);
        }

        return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);
    }

    public async Task<Result<bool, KeySplittingFailure>> HasStoredSharesAsync(Guid membershipId)
    {
        if (_memoryShareCache.ContainsKey(membershipId))
            return Result<bool, KeySplittingFailure>.Ok(true);

        string identifier = membershipId.ToString();
        Result<Option<byte[]>, InternalServiceApiFailure> localShare =
            await _secureStorageProvider.TryGetByKeyAsync($"{StorageKeyConstants.Share.LocalPrefix}{identifier}");

        return Result<bool, KeySplittingFailure>.Ok(localShare.IsOk && localShare.Unwrap().HasValue);
    }

    public async Task<Result<byte[], KeySplittingFailure>> StoreAndReconstructKeyAsync(
        byte[] originalKey,
        Guid membershipId,
        int threshold = ShareDistributionConstants.DefaultMinimumThreshold,
        int totalShares = ShareDistributionConstants.DefaultTotalShares)
    {
        Result<SodiumSecureMemoryHandle, Ecliptix.Utilities.Failures.Sodium.SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(originalKey.Length);
        if (allocResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.AllocationFailed(allocResult.UnwrapErr().Message));

        using SodiumSecureMemoryHandle originalKeyHandle = allocResult.Unwrap();
        Result<Unit, Ecliptix.Utilities.Failures.Sodium.SodiumFailure> writeResult =
            originalKeyHandle.Write(originalKey);
        if (writeResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.MemoryWriteFailed(writeResult.UnwrapErr().Message));

        Result<KeySplitResult, KeySplittingFailure> splitResult =
            await _keySplitter.SplitKeyAsync(originalKeyHandle, threshold, totalShares, hmacKeyHandle: null);
        if (splitResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(splitResult.UnwrapErr());

        using KeySplitResult splitKeys = splitResult.Unwrap();

        Result<Unit, KeySplittingFailure> storeResult = await StoreKeySharesAsync(splitKeys, membershipId);
        if (storeResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(storeResult.UnwrapErr());

        Result<KeyShare[], KeySplittingFailure> retrieveResult = await RetrieveKeySharesAsync(membershipId, threshold);
        if (retrieveResult.IsErr)
            return Result<byte[], KeySplittingFailure>.Err(retrieveResult.UnwrapErr());

        KeyShare[] shares = retrieveResult.Unwrap();
        try
        {
            Result<SodiumSecureMemoryHandle, KeySplittingFailure> reconstructHandleResult =
                await _keySplitter.ReconstructKeyHandleAsync(shares, hmacKeyHandle: null);
            if (reconstructHandleResult.IsErr)
                return Result<byte[], KeySplittingFailure>.Err(reconstructHandleResult.UnwrapErr());

            using SodiumSecureMemoryHandle reconstructedHandle = reconstructHandleResult.Unwrap();
            Result<byte[], Ecliptix.Utilities.Failures.Sodium.SodiumFailure> readResult =
                reconstructedHandle.ReadBytes(reconstructedHandle.Length);

            if (readResult.IsErr)
                return Result<byte[], KeySplittingFailure>.Err(
                    KeySplittingFailure.MemoryReadFailed(readResult.UnwrapErr().Message));

            return Result<byte[], KeySplittingFailure>.Ok(readResult.Unwrap());
        }
        finally
        {
            foreach (KeyShare share in shares)
            {
                share.Dispose();
            }
        }
    }

    private async Task<Result<Unit, KeySplittingFailure>> StoreShareByLocationAsync(KeyShare share, Guid membershipId,
        int shareIndex)
    {
        string identifier = membershipId.ToString();
        string shareKey = $"{GetSharePrefix(shareIndex)}_share_{identifier}_{shareIndex}";

        switch (shareIndex)
        {
            case 0:
                if (_platformSecurityProvider.IsHardwareSecurityAvailable())
                {
                    byte[] encrypted = await _platformSecurityProvider.HardwareEncryptAsync(share.ShareData);
                    await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, encrypted);
                    CryptographicOperations.ZeroMemory(encrypted);
                }
                else
                {
                    await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, share.ShareData);
                }

                break;

            case 1:
                await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, share.ShareData);
                break;

            case 2:
                await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, share.ShareData);
                break;

            case 3:
                uint encryptId = (uint)membershipId.GetHashCode();
                byte[] doubleEncrypted = await DoubleEncryptAsync(share.ShareData, encryptId, shareIndex);
                Result<Unit, InternalServiceApiFailure> storeResult =
                    await _secureStorageProvider.StoreAsync($"{StorageKeyConstants.Share.LocalPrefix}{identifier}", doubleEncrypted);

                CryptographicOperations.ZeroMemory(doubleEncrypted);

                if (storeResult.IsErr)
                    return Result<Unit, KeySplittingFailure>.Err(KeySplittingFailure.ShareStorageFailed(shareIndex, storeResult.UnwrapErr().Message));

                break;

            case 4:
                await _platformSecurityProvider.StoreKeyInKeychainAsync($"backup_{shareKey}", share.ShareData);
                break;
        }

        return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);
    }

    private async Task<Result<KeyShare, KeySplittingFailure>> RetrieveShareByLocationAsync(Guid membershipId, int shareIndex)
    {
        string identifier = membershipId.ToString();
        string shareKey = $"{GetSharePrefix(shareIndex)}_share_{identifier}_{shareIndex}";
        byte[]? shareData = null;

        switch (shareIndex)
        {
            case 0:
                byte[]? hwShare = await _platformSecurityProvider.GetKeyFromKeychainAsync(shareKey);
                if (hwShare != null)
                {
                    if (_platformSecurityProvider.IsHardwareSecurityAvailable())
                    {
                        shareData = await _platformSecurityProvider.HardwareDecryptAsync(hwShare);
                    }
                    else
                    {
                        shareData = hwShare;
                    }
                }

                break;

            case 1:
                shareData = await _platformSecurityProvider.GetKeyFromKeychainAsync(shareKey);
                break;

            case 2:
                shareData = await _platformSecurityProvider.GetKeyFromKeychainAsync(shareKey);
                break;

            case 3:
                Result<Option<byte[]>, InternalServiceApiFailure> getResult =
                    await _secureStorageProvider.TryGetByKeyAsync($"{StorageKeyConstants.Share.LocalPrefix}{identifier}");

                if (getResult.IsOk && getResult.Unwrap().HasValue)
                {
                    byte[]? value = getResult.Unwrap().Value;
                    if (value != null)
                    {
                        Result<byte[], KeySplittingFailure> decryptResult = await DoubleDecryptAsync(value);
                        if (decryptResult.IsOk)
                        {
                            shareData = decryptResult.Unwrap();
                        }
                    }
                }

                break;

            case 4:
                shareData = await _platformSecurityProvider.GetKeyFromKeychainAsync($"backup_{shareKey}");
                break;
        }

        if (shareData == null)
            return Result<KeyShare, KeySplittingFailure>.Err(KeySplittingFailure.ShareNotFound(shareIndex));

        ShareLocation location = (ShareLocation)shareIndex;
        int shamirShareNumber = shareIndex + 1;
        return Result<KeyShare, KeySplittingFailure>.Ok(new KeyShare(shareData, shamirShareNumber, location));
    }

    private static string GetSharePrefix(int shareIndex) => shareIndex switch
    {
        0 => StorageKeyConstants.Share.HardwarePrefix[..^1],
        1 => StorageKeyConstants.Share.KeychainPrefix[..^1],
        2 => StorageKeyConstants.Share.MemoryPrefix,
        3 => StorageKeyConstants.Share.LocalPrefix[..^1],
        4 => StorageKeyConstants.Share.BackupPrefix[..^1],
        _ => "unknown"
    };

    private async Task<byte[]> DoubleEncryptAsync(byte[] data, uint connectId, int shareIndex)
    {
        byte[]? encryptionKey = null;
        byte[]? platformEncrypted = null;
        string? keyIdentifier = null;

        try
        {
            platformEncrypted = _platformSecurityProvider.IsHardwareSecurityAvailable()
                ? await _platformSecurityProvider.HardwareEncryptAsync(data)
                : data;

            using Aes aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();
            encryptionKey = new byte[aes.Key.Length];
            aes.Key.CopyTo(encryptionKey, 0);

            keyIdentifier = $"{StorageKeyConstants.Share.EcliptixSharePrefix}{connectId}_{shareIndex}_{DateTime.UtcNow.Ticks}";

            await _platformSecurityProvider.StoreKeyInKeychainAsync(keyIdentifier, encryptionKey);

            string trackerKey = $"{connectId}_{shareIndex}";
            _keychainTracker.AddOrUpdate(trackerKey,
                [keyIdentifier],
                (_, existing) =>
                {
                    existing.Add(keyIdentifier);
                    return existing;
                });

            byte[] encrypted = aes.EncryptCbc(platformEncrypted, aes.IV);

            byte[] keyIdBytes = Encoding.UTF8.GetBytes(keyIdentifier);
            byte[] result = new byte[4 + keyIdBytes.Length + aes.IV.Length + encrypted.Length];

            BitConverter.GetBytes(keyIdBytes.Length).CopyTo(result, 0);
            keyIdBytes.CopyTo(result, 4);
            aes.IV.CopyTo(result, 4 + keyIdBytes.Length);
            encrypted.CopyTo(result, 4 + keyIdBytes.Length + aes.IV.Length);

            return result;
        }
        catch
        {
            if (keyIdentifier != null)
            {
                await CleanupKeychainEntryAsync(keyIdentifier, connectId, shareIndex);
            }

            throw;
        }
        finally
        {
            if (encryptionKey != null)
                CryptographicOperations.ZeroMemory(encryptionKey);
            if (platformEncrypted != null && platformEncrypted != data)
                CryptographicOperations.ZeroMemory(platformEncrypted);
        }
    }

    private async Task<Result<byte[], KeySplittingFailure>> DoubleDecryptAsync(byte[] encryptedData)
    {
        byte[]? key = null;
        byte[]? platformEncrypted = null;

        try
        {
            if (encryptedData == null || encryptedData.Length < 4)
                return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.InvalidDataFormat("Invalid encrypted data format"));

            int keyIdLength = BitConverter.ToInt32(encryptedData, 0);

            if (keyIdLength is <= ShareDistributionConstants.KeyIdLengthMin or > ShareDistributionConstants.KeyIdLengthMax)
                return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.InvalidDataFormat($"Invalid key identifier length: {keyIdLength}"));

            if (encryptedData.Length < 4 || encryptedData.Length - 4 < keyIdLength)
                return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.InvalidDataFormat("Invalid key identifier length: insufficient data"));

            byte[] keyIdBytes = new byte[keyIdLength];
            Array.Copy(encryptedData, 4, keyIdBytes, 0, keyIdLength);
            string? keyIdentifier = Encoding.UTF8.GetString(keyIdBytes);

            key = await _platformSecurityProvider.GetKeyFromKeychainAsync(keyIdentifier);
            if (key == null)
                return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.KeyNotFoundInKeychain($"Encryption key not found in keychain: {keyIdentifier}"));

            using Aes aes = Aes.Create();
            aes.Key = key;

            byte[] iv = new byte[CryptographicConstants.AesIvSize];
            int ivOffset = 4 + keyIdLength;

            if (ivOffset + CryptographicConstants.AesIvSize > encryptedData.Length)
                return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.InvalidDataFormat("Encrypted data truncated: missing IV"));

            Array.Copy(encryptedData, ivOffset, iv, 0, CryptographicConstants.AesIvSize);
            aes.IV = iv;

            int ciphertextOffset = ivOffset + CryptographicConstants.AesIvSize;
            if (ciphertextOffset >= encryptedData.Length)
                return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.InvalidDataFormat("Encrypted data truncated: missing ciphertext"));

            byte[] ciphertext = new byte[encryptedData.Length - ciphertextOffset];
            Array.Copy(encryptedData, ciphertextOffset, ciphertext, 0, ciphertext.Length);

            platformEncrypted = aes.DecryptCbc(ciphertext, iv);

            byte[] plaintext = _platformSecurityProvider.IsHardwareSecurityAvailable()
                ? await _platformSecurityProvider.HardwareDecryptAsync(platformEncrypted)
                : platformEncrypted;

            return Result<byte[], KeySplittingFailure>.Ok(plaintext);
        }
        finally
        {
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
            if (platformEncrypted != null)
                CryptographicOperations.ZeroMemory(platformEncrypted);
        }
    }

    private async Task CleanupKeychainEntryAsync(string keyIdentifier, uint connectId, int shareIndex)
    {
        await _platformSecurityProvider.DeleteKeyFromKeychainAsync(keyIdentifier);

        string trackerKey = $"{connectId}_{shareIndex}";
        if (_keychainTracker.TryGetValue(trackerKey, out HashSet<string>? entries))
        {
            entries.Remove(keyIdentifier);
            if (entries.Count == 0)
            {
                _keychainTracker.TryRemove(trackerKey, out _);
            }
        }
    }

    private async Task CleanupKeychainEntriesForIdentifierAsync(string identifier)
    {
        List<string> keysToRemove = [];

        foreach (string trackerKey in _keychainTracker.Keys)
        {
            if (trackerKey.StartsWith($"{identifier}_"))
            {
                keysToRemove.Add(trackerKey);
            }
        }

        foreach (string trackerKey in keysToRemove)
        {
            if (_keychainTracker.TryRemove(trackerKey, out HashSet<string>? entries))
            {
                foreach (string keyIdentifier in entries)
                {
                    await _platformSecurityProvider.DeleteKeyFromKeychainAsync(keyIdentifier);
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        lock (_storageLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        List<Task> cleanupTasks = new();
        foreach (KeyValuePair<string, HashSet<string>> tracker in _keychainTracker)
        {
            foreach (string keyIdentifier in tracker.Value)
            {
                cleanupTasks.Add(_platformSecurityProvider.DeleteKeyFromKeychainAsync(keyIdentifier));
            }
        }

        await Task.WhenAll(cleanupTasks);
        _keychainTracker.Clear();

        _cacheLock.EnterWriteLock();
        try
        {
            foreach (KeyValuePair<Guid, SodiumSecureMemoryHandle> kvp in _memoryShareCache)
            {
                kvp.Value?.Dispose();
            }

            _memoryShareCache.Clear();
            _memoryCacheAccessTimes.Clear();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        _cacheLock.Dispose();
    }

    public void Dispose()
    {
        lock (_storageLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        foreach (KeyValuePair<string, HashSet<string>> tracker in _keychainTracker)
        {
            foreach (string keyIdentifier in tracker.Value)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _platformSecurityProvider.DeleteKeyFromKeychainAsync(keyIdentifier);
                    }
                    catch
                    {
                    }
                });
            }
        }

        _keychainTracker.Clear();

        _cacheLock.EnterWriteLock();
        try
        {
            foreach (KeyValuePair<Guid, SodiumSecureMemoryHandle> kvp in _memoryShareCache)
            {
                kvp.Value?.Dispose();
            }

            _memoryShareCache.Clear();
            _memoryCacheAccessTimes.Clear();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        _cacheLock.Dispose();
    }

    public async Task<Result<Unit, KeySplittingFailure>> ClearAllCacheAsync()
    {
        _cacheLock.EnterWriteLock();
        try
        {
            foreach (KeyValuePair<Guid, SodiumSecureMemoryHandle> entry in _memoryShareCache)
            {
                entry.Value?.Dispose();
            }

            _memoryShareCache.Clear();
            _memoryCacheAccessTimes.Clear();
        }
        finally
        {
            _cacheLock.ExitWriteLock();
        }

        List<Task> cleanupTasks = [];
        foreach (KeyValuePair<string, HashSet<string>> trackerEntry in _keychainTracker)
        {
            foreach (string keychainKey in trackerEntry.Value)
            {
                cleanupTasks.Add(_platformSecurityProvider.DeleteKeyFromKeychainAsync(keychainKey));
            }
        }

        await Task.WhenAll(cleanupTasks);
        _keychainTracker.Clear();

        return Result<Unit, KeySplittingFailure>.Ok(Unit.Value);
    }
}