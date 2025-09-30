using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public sealed class MultiLocationKeyStorage : IMultiLocationKeyStorage, IDisposable
{
    private readonly IPlatformSecurityProvider _platformSecurityProvider;
    private readonly IApplicationSecureStorageProvider _secureStorageProvider;
    private readonly ISecureKeySplitter _keySplitter;
    private readonly ConcurrentDictionary<uint, SodiumSecureMemoryHandle> _memoryShareCache = new();
    private readonly ConcurrentDictionary<uint, DateTime> _memoryCacheAccessTimes = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _keychainTracker = new();
    private readonly ReaderWriterLockSlim _cacheLock = new();
    private readonly object _storageLock = new();
    private bool _disposed;

    public MultiLocationKeyStorage(
        IPlatformSecurityProvider platformSecurityProvider,
        IApplicationSecureStorageProvider secureStorageProvider,
        ISecureKeySplitter keySplitter)
    {
        _platformSecurityProvider = platformSecurityProvider ?? throw new ArgumentNullException(nameof(platformSecurityProvider));
        _secureStorageProvider = secureStorageProvider ?? throw new ArgumentNullException(nameof(secureStorageProvider));
        _keySplitter = keySplitter ?? throw new ArgumentNullException(nameof(keySplitter));
    }

    public async Task<Result<Unit, string>> StoreKeySharesAsync(KeySplitResult splitKeys, uint connectId)
    {
        return await StoreKeySharesAsync(splitKeys, connectId.ToString());
    }

    public async Task<Result<Unit, string>> StoreKeySharesAsync(KeySplitResult splitKeys, string identifier)
    {
        if (splitKeys == null)
            return Result<Unit, string>.Err("Split keys cannot be null");

        if (string.IsNullOrEmpty(identifier))
            return Result<Unit, string>.Err("Identifier cannot be null or empty");

        lock (_storageLock)
        {
            if (_disposed)
                return Result<Unit, string>.Err("Storage service is disposed");
        }

        try
        {
            Log.Information("Storing {ShareCount} key shares for identifier {Identifier}", splitKeys.Shares.Length, identifier);

            // Parse identifier to check if it's a uint for backward compatibility
            uint connectId = 0;
            bool isConnectId = uint.TryParse(identifier, out connectId);

            List<Task<Result<Unit, string>>> storageTasks = new();

            for (int i = 0; i < splitKeys.Shares.Length && i < 5; i++)
            {
                KeyShare share = splitKeys.Shares[i];
                // Use connectId if available for memory cache compatibility, otherwise use string
                storageTasks.Add(isConnectId
                    ? StoreShareByLocationAsync(share, connectId, i)
                    : StoreShareByLocationStringAsync(share, identifier, i));
            }

            Result<Unit, string>[] results = await Task.WhenAll(storageTasks);

            int successCount = results.Count(r => r.IsOk);
            if (successCount < splitKeys.Threshold)
            {
                await RemoveKeySharesAsync(identifier);
                return Result<Unit, string>.Err($"Failed to store minimum required shares. Only {successCount} of {splitKeys.Threshold} succeeded");
            }

            Log.Information("Successfully stored {SuccessCount} of {TotalShares} shares for identifier {Identifier}",
                successCount, splitKeys.Shares.Length, identifier);

            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store key shares for identifier {Identifier}", identifier);
            return Result<Unit, string>.Err($"Key share storage failed: {ex.Message}");
        }
    }

    public async Task<Result<KeyShare[], string>> RetrieveKeySharesAsync(uint connectId, int minimumShares = 3)
    {
        return await RetrieveKeySharesAsync(connectId.ToString(), minimumShares);
    }

    public async Task<Result<KeyShare[], string>> RetrieveKeySharesAsync(string identifier, int minimumShares = 3)
    {
        lock (_storageLock)
        {
            if (_disposed)
                return Result<KeyShare[], string>.Err("Storage service is disposed");
        }

        try
        {
            Log.Debug("Retrieving key shares for identifier {Identifier}", identifier);

            // Parse identifier to check if it's a uint
            uint connectId = 0;
            bool isConnectId = uint.TryParse(identifier, out connectId);

            List<Task<Result<KeyShare, string>>> retrievalTasks = new();

            for (int i = 0; i < 5; i++)
            {
                retrievalTasks.Add(isConnectId
                    ? RetrieveShareByLocationAsync(connectId, i)
                    : RetrieveShareByLocationStringAsync(identifier, i));
            }

            Result<KeyShare, string>[] results = await Task.WhenAll(retrievalTasks);

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
                return Result<KeyShare[], string>.Err($"Insufficient shares retrieved. Got {retrievedShares.Count}, need {minimumShares}");
            }

            Log.Information("Successfully retrieved {ShareCount} shares for identifier {Identifier}",
                retrievedShares.Count, identifier);

            return Result<KeyShare[], string>.Ok(retrievedShares.Take(minimumShares).ToArray());
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve key shares for identifier {Identifier}", identifier);
            return Result<KeyShare[], string>.Err($"Key share retrieval failed: {ex.Message}");
        }
    }

    public async Task<Result<Unit, string>> RemoveKeySharesAsync(uint connectId)
    {
        return await RemoveKeySharesAsync(connectId.ToString());
    }

    public async Task<Result<Unit, string>> RemoveKeySharesAsync(string identifier)
    {
        try
        {
            Log.Debug("Removing all key shares for identifier {Identifier}", identifier);

            // Clean up all keychain entries for this identifier
            await CleanupKeychainEntriesForIdentifierAsync(identifier);

            List<Task> removalTasks = new();

            if (_platformSecurityProvider.IsHardwareSecurityAvailable())
            {
                removalTasks.Add(_platformSecurityProvider.DeleteKeyFromKeychainAsync($"hw_share_{identifier}_0"));
            }

            for (int i = 1; i <= 4; i++)
            {
                removalTasks.Add(_platformSecurityProvider.DeleteKeyFromKeychainAsync($"kc_share_{identifier}_{i}"));
            }

            // Handle memory cache for numeric identifiers
            if (uint.TryParse(identifier, out uint connectId))
            {
                _cacheLock.EnterWriteLock();
                try
                {
                    if (_memoryShareCache.TryRemove(connectId, out SodiumSecureMemoryHandle? memHandle))
                    {
                        memHandle?.Dispose();
                        _memoryCacheAccessTimes.TryRemove(connectId, out _);
                    }
                }
                finally
                {
                    _cacheLock.ExitWriteLock();
                }
            }

            Result<Unit, InternalServiceApiFailure> localRemoval = _secureStorageProvider.DeleteAsync($"local_share_{identifier}");
            if (localRemoval.IsErr)
            {
                Log.Warning("Failed to remove local share for connection {ConnectId}", connectId);
            }

            await Task.WhenAll(removalTasks);

            Log.Information("Removed all key shares for identifier {Identifier}", identifier);
            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to remove key shares for identifier {Identifier}", identifier);
            return Result<Unit, string>.Err($"Key share removal failed: {ex.Message}");
        }
    }

    public async Task<Result<bool, string>> HasStoredSharesAsync(uint connectId)
    {
        return await HasStoredSharesAsync(connectId.ToString());
    }

    public async Task<Result<bool, string>> HasStoredSharesAsync(string identifier)
    {
        try
        {
            // Check memory cache only for numeric identifiers
            if (uint.TryParse(identifier, out uint cacheKey) && _memoryShareCache.ContainsKey(cacheKey))
                return Result<bool, string>.Ok(true);

            Result<Option<byte[]>, InternalServiceApiFailure> localShare =
                await _secureStorageProvider.TryGetByKeyAsync($"local_share_{identifier}");

            return Result<bool, string>.Ok(localShare.IsOk && localShare.Unwrap().HasValue);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check for stored shares for identifier {Identifier}", identifier);
            return Result<bool, string>.Err($"Share check failed: {ex.Message}");
        }
    }

    public async Task<Result<byte[], string>> StoreAndReconstructKeyAsync(
        byte[] originalKey,
        uint connectId,
        int threshold = 3,
        int totalShares = 5)
    {
        Result<KeySplitResult, string> splitResult = await _keySplitter.SplitKeyAsync(originalKey, threshold, totalShares);
        if (splitResult.IsErr)
            return Result<byte[], string>.Err(splitResult.UnwrapErr());

        using KeySplitResult splitKeys = splitResult.Unwrap();

        Result<Unit, string> storeResult = await StoreKeySharesAsync(splitKeys, connectId);
        if (storeResult.IsErr)
            return Result<byte[], string>.Err(storeResult.UnwrapErr());

        Result<KeyShare[], string> retrieveResult = await RetrieveKeySharesAsync(connectId, threshold);
        if (retrieveResult.IsErr)
            return Result<byte[], string>.Err(retrieveResult.UnwrapErr());

        KeyShare[] shares = retrieveResult.Unwrap();
        try
        {
            return await _keySplitter.ReconstructKeyAsync(shares);
        }
        finally
        {
            foreach (KeyShare share in shares)
            {
                share?.Dispose();
            }
        }
    }

    private async Task<Result<Unit, string>> StoreShareByLocationAsync(KeyShare share, uint connectId, int shareIndex)
    {
        return await StoreShareByLocationStringAsync(share, connectId.ToString(), shareIndex, connectId);
    }

    private async Task<Result<Unit, string>> StoreShareByLocationStringAsync(KeyShare share, string identifier, int shareIndex, uint? connectIdForCache = null)
    {
        try
        {
            string shareKey = $"{GetSharePrefix(shareIndex)}_share_{identifier}_{shareIndex}";

            switch (shareIndex)
            {
                case 0: // Hardware Security
                    if (_platformSecurityProvider.IsHardwareSecurityAvailable())
                    {
                        byte[] encrypted = await _platformSecurityProvider.HardwareEncryptAsync(share.ShareData);
                        await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, encrypted);
                        CryptographicOperations.ZeroMemory(encrypted);
                        Log.Debug("Stored share {ShareIndex} in hardware security", shareIndex);
                    }
                    else
                    {
                        await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, share.ShareData);
                        Log.Debug("Stored share {ShareIndex} in keychain (hardware unavailable)", shareIndex);
                    }
                    break;

                case 1: // Platform Keychain
                    await _platformSecurityProvider.StoreKeyInKeychainAsync(shareKey, share.ShareData);
                    Log.Debug("Stored share {ShareIndex} in platform keychain", shareIndex);
                    break;

                case 2: // Secure Memory
                    Result<SodiumSecureMemoryHandle, Utilities.Failures.Sodium.SodiumFailure> allocResult =
                        SodiumSecureMemoryHandle.Allocate(share.ShareData.Length);

                    if (allocResult.IsOk)
                    {
                        SodiumSecureMemoryHandle handle = allocResult.Unwrap();
                        Result<Unit, Utilities.Failures.Sodium.SodiumFailure> writeResult = handle.Write(share.ShareData);

                        if (writeResult.IsOk)
                        {
                            // Use proper locking for cache operations
                            _cacheLock.EnterWriteLock();
                            try
                            {
                                // Only use memory cache if we have a numeric connectId
                                if (connectIdForCache.HasValue)
                                {
                                    // Properly dispose old handle before replacing
                                    if (_memoryShareCache.TryRemove(connectIdForCache.Value, out SodiumSecureMemoryHandle? oldHandle))
                                    {
                                        oldHandle?.Dispose();
                                        Log.Debug("Disposed old secure memory handle for connection {ConnectId}", connectIdForCache.Value);
                                    }
                                }

                                // Proper LRU cache eviction
                                if (_memoryShareCache.Count >= 100) // Limit to 100 concurrent sessions
                                {
                                    Log.Warning("Memory share cache at capacity, cleaning up LRU entries");

                                    // Find the 10 least recently used entries
                                    var lruEntries = _memoryCacheAccessTimes
                                        .OrderBy(kvp => kvp.Value)
                                        .Take(10)
                                        .Select(kvp => new { Key = kvp.Key, AccessTime = kvp.Value })
                                        .ToArray();

                                    foreach (var entry in lruEntries)
                                    {
                                        if (_memoryShareCache.TryRemove(entry.Key, out SodiumSecureMemoryHandle? oldEntry))
                                        {
                                            oldEntry?.Dispose();
                                            _memoryCacheAccessTimes.TryRemove(entry.Key, out _);
                                            Log.Debug("Evicted LRU secure memory entry for connection {ConnectId} (last accessed: {AccessTime})",
                                                entry.Key, entry.AccessTime);
                                        }
                                    }
                                }

                                // Only store in memory cache if we have a numeric connectId
                                if (connectIdForCache.HasValue)
                                {
                                    _memoryShareCache[connectIdForCache.Value] = handle;
                                    _memoryCacheAccessTimes[connectIdForCache.Value] = DateTime.UtcNow;
                                }
                                else
                                {
                                    // For non-numeric identifiers, we don't use memory cache
                                    // The share will be stored in other locations only
                                    handle.Dispose();
                                }
                                Log.Debug("Stored share {ShareIndex} in secure memory", shareIndex);
                            }
                            finally
                            {
                                _cacheLock.ExitWriteLock();
                            }
                        }
                        else
                        {
                            handle.Dispose();
                            return Result<Unit, string>.Err($"Failed to write to secure memory: {writeResult.UnwrapErr().Message}");
                        }
                    }
                    else
                    {
                        return Result<Unit, string>.Err($"Failed to allocate secure memory: {allocResult.UnwrapErr().Message}");
                    }
                    break;

                case 3: // Local Encrypted Storage
                    uint encryptId = connectIdForCache ?? (uint)identifier.GetHashCode();
                    byte[] doubleEncrypted = await DoubleEncryptAsync(share.ShareData, encryptId, shareIndex);
                    Result<Unit, InternalServiceApiFailure> storeResult =
                        await _secureStorageProvider.StoreAsync($"local_share_{identifier}", doubleEncrypted);

                    CryptographicOperations.ZeroMemory(doubleEncrypted);

                    if (storeResult.IsErr)
                        return Result<Unit, string>.Err(storeResult.UnwrapErr().Message);

                    Log.Debug("Stored share {ShareIndex} in local encrypted storage", shareIndex);
                    break;

                case 4: // Backup (optional)
                    await _platformSecurityProvider.StoreKeyInKeychainAsync($"backup_{shareKey}", share.ShareData);
                    Log.Debug("Stored share {ShareIndex} in backup location", shareIndex);
                    break;
            }

            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to store share {ShareIndex} for identifier {Identifier}", shareIndex, identifier);
            return Result<Unit, string>.Err($"Failed to store share {shareIndex}: {ex.Message}");
        }
    }

    private async Task<Result<KeyShare, string>> RetrieveShareByLocationAsync(uint connectId, int shareIndex)
    {
        return await RetrieveShareByLocationStringAsync(connectId.ToString(), shareIndex, connectId);
    }

    private async Task<Result<KeyShare, string>> RetrieveShareByLocationStringAsync(string identifier, int shareIndex, uint? connectIdForCache = null)
    {
        try
        {
            string shareKey = $"{GetSharePrefix(shareIndex)}_share_{identifier}_{shareIndex}";
            byte[]? shareData = null;

            switch (shareIndex)
            {
                case 0: // Hardware Security
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

                case 1: // Platform Keychain
                    shareData = await _platformSecurityProvider.GetKeyFromKeychainAsync(shareKey);
                    break;

                case 2: // Secure Memory
                    if (!connectIdForCache.HasValue)
                        break; // No memory cache for string identifiers

                    _cacheLock.EnterReadLock();
                    try
                    {
                        if (_memoryShareCache.TryGetValue(connectIdForCache.Value, out SodiumSecureMemoryHandle? memHandle))
                        {
                            // Update access time for LRU tracking
                            _cacheLock.ExitReadLock();
                            _cacheLock.EnterWriteLock();
                            try
                            {
                                _memoryCacheAccessTimes[connectIdForCache.Value] = DateTime.UtcNow;
                            }
                            finally
                            {
                                _cacheLock.ExitWriteLock();
                            }
                            _cacheLock.EnterReadLock();

                            Result<byte[], Utilities.Failures.Sodium.SodiumFailure> readResult =
                                memHandle.WithReadAccess<byte[]>(span =>
                                {
                                    // Create a copy - we need to return the data
                                    byte[] data = new byte[span.Length];
                                    span.CopyTo(data);
                                    return Result<byte[], Utilities.Failures.Sodium.SodiumFailure>.Ok(data);
                                });

                            if (readResult.IsOk)
                            {
                                shareData = readResult.Unwrap();
                            }
                        }
                    }
                    finally
                    {
                        _cacheLock.ExitReadLock();
                    }
                    break;

                case 3: // Local Encrypted Storage
                    Result<Option<byte[]>, InternalServiceApiFailure> getResult =
                        await _secureStorageProvider.TryGetByKeyAsync($"local_share_{identifier}");

                    if (getResult.IsOk && getResult.Unwrap().HasValue)
                    {
                        byte[]? value = getResult.Unwrap().Value;
                        if (value != null)
                            shareData = await DoubleDecryptAsync(value);
                    }
                    break;

                case 4: // Backup
                    shareData = await _platformSecurityProvider.GetKeyFromKeychainAsync($"backup_{shareKey}");
                    break;
            }

            if (shareData == null)
                return Result<KeyShare, string>.Err($"Share {shareIndex} not found");

            // FIX: Document share indexing
            // Storage uses 0-based indexing (0-4 for 5 locations)
            // Shamir uses 1-based share numbers (1-5)
            // KeyShare.ShareNumber should be 1-based for Shamir compatibility
            ShareLocation location = (ShareLocation)shareIndex;
            int shamirShareNumber = shareIndex + 1; // Convert 0-based to 1-based
            return Result<KeyShare, string>.Ok(new KeyShare(shareData, shamirShareNumber, location));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retrieve share {ShareIndex} for identifier {Identifier}", shareIndex, identifier);
            return Result<KeyShare, string>.Err($"Failed to retrieve share {shareIndex}: {ex.Message}");
        }
    }

    private static string GetSharePrefix(int shareIndex) => shareIndex switch
    {
        0 => "hw",
        1 => "kc",
        2 => "mem",
        3 => "local",
        4 => "backup",
        _ => "unknown"
    };

    private async Task<byte[]> DoubleEncryptAsync(byte[] data, uint connectId, int shareIndex)
    {
        byte[]? encryptionKey = null;
        byte[]? platformEncrypted = null;
        string? keyIdentifier = null;

        try
        {
            // First layer: Platform-specific encryption
            platformEncrypted = _platformSecurityProvider.IsHardwareSecurityAvailable()
                ? await _platformSecurityProvider.HardwareEncryptAsync(data)
                : data;

            // Second layer: Additional AES encryption with key stored in keychain
            using Aes aes = Aes.Create();
            aes.GenerateKey();
            aes.GenerateIV();
            encryptionKey = new byte[aes.Key.Length];
            aes.Key.CopyTo(encryptionKey, 0);

            // Generate unique key identifier
            keyIdentifier = $"ecliptix_share_{connectId}_{shareIndex}_{DateTime.UtcNow.Ticks}";

            // Store the encryption key securely in platform keychain
            await _platformSecurityProvider.StoreKeyInKeychainAsync(keyIdentifier, encryptionKey);

            // Track this keychain entry for cleanup
            string trackerKey = $"{connectId}_{shareIndex}";
            _keychainTracker.AddOrUpdate(trackerKey,
                new HashSet<string> { keyIdentifier },
                (_, existing) =>
                {
                    existing.Add(keyIdentifier);
                    return existing;
                });

            byte[] encrypted = aes.EncryptCbc(platformEncrypted, aes.IV);

            // Store key identifier (not the key!) with IV and ciphertext
            byte[] keyIdBytes = Encoding.UTF8.GetBytes(keyIdentifier);
            byte[] result = new byte[4 + keyIdBytes.Length + aes.IV.Length + encrypted.Length];

            // Format: [keyId length (4 bytes)][keyId][IV][ciphertext]
            BitConverter.GetBytes(keyIdBytes.Length).CopyTo(result, 0);
            keyIdBytes.CopyTo(result, 4);
            aes.IV.CopyTo(result, 4 + keyIdBytes.Length);
            encrypted.CopyTo(result, 4 + keyIdBytes.Length + aes.IV.Length);

            return result;
        }
        catch
        {
            // Clean up keychain entry on failure
            if (keyIdentifier != null)
            {
                await CleanupKeychainEntryAsync(keyIdentifier, connectId, shareIndex);
            }
            throw;
        }
        finally
        {
            // Zero out sensitive data
            if (encryptionKey != null)
                CryptographicOperations.ZeroMemory(encryptionKey);
            if (platformEncrypted != null && platformEncrypted != data)
                CryptographicOperations.ZeroMemory(platformEncrypted);
        }
    }

    private async Task<byte[]> DoubleDecryptAsync(byte[] encryptedData)
    {
        byte[]? key = null;
        byte[]? platformEncrypted = null;
        string? keyIdentifier = null;

        try
        {
            // Validate input
            if (encryptedData == null || encryptedData.Length < 4)
                throw new ArgumentException("Invalid encrypted data format");

            // Extract key identifier length
            int keyIdLength = BitConverter.ToInt32(encryptedData, 0);

            // Validate key identifier length
            if (keyIdLength <= 0 || keyIdLength > 256 || 4 + keyIdLength > encryptedData.Length)
                throw new InvalidOperationException($"Invalid key identifier length: {keyIdLength}");

            // Extract key identifier
            byte[] keyIdBytes = new byte[keyIdLength];
            Array.Copy(encryptedData, 4, keyIdBytes, 0, keyIdLength);
            keyIdentifier = Encoding.UTF8.GetString(keyIdBytes);

            // Retrieve the encryption key from keychain
            key = await _platformSecurityProvider.GetKeyFromKeychainAsync(keyIdentifier);
            if (key == null)
                throw new InvalidOperationException($"Encryption key not found in keychain: {keyIdentifier}");

            using Aes aes = Aes.Create();
            aes.Key = key;

            // Extract IV and ciphertext
            byte[] iv = new byte[16];
            int ivOffset = 4 + keyIdLength;

            if (ivOffset + 16 > encryptedData.Length)
                throw new InvalidOperationException("Encrypted data truncated: missing IV");

            Array.Copy(encryptedData, ivOffset, iv, 0, 16);
            aes.IV = iv;

            int ciphertextOffset = ivOffset + 16;
            if (ciphertextOffset >= encryptedData.Length)
                throw new InvalidOperationException("Encrypted data truncated: missing ciphertext");

            byte[] ciphertext = new byte[encryptedData.Length - ciphertextOffset];
            Array.Copy(encryptedData, ciphertextOffset, ciphertext, 0, ciphertext.Length);

            // Second layer decryption
            platformEncrypted = aes.DecryptCbc(ciphertext, iv);

            // First layer decryption
            byte[] plaintext = _platformSecurityProvider.IsHardwareSecurityAvailable()
                ? await _platformSecurityProvider.HardwareDecryptAsync(platformEncrypted)
                : platformEncrypted;

            // Don't delete the key here - it's managed by cleanup methods
            // This allows for re-reads without recreating keys

            return plaintext;
        }
        finally
        {
            // Zero out sensitive data
            if (key != null)
                CryptographicOperations.ZeroMemory(key);
            if (platformEncrypted != null)
                CryptographicOperations.ZeroMemory(platformEncrypted);
        }
    }

    private async Task CleanupKeychainEntryAsync(string keyIdentifier, uint connectId, int shareIndex)
    {
        try
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
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to cleanup keychain entry {KeyId}", keyIdentifier);
        }
    }

    private async Task CleanupKeychainEntriesForConnectionAsync(uint connectId)
    {
        await CleanupKeychainEntriesForIdentifierAsync(connectId.ToString());
    }

    private async Task CleanupKeychainEntriesForIdentifierAsync(string identifier)
    {
        List<string> keysToRemove = new();

        // Find all tracker keys for this identifier
        foreach (string trackerKey in _keychainTracker.Keys)
        {
            if (trackerKey.StartsWith($"{identifier}_"))
            {
                keysToRemove.Add(trackerKey);
            }
        }

        // Clean up all keychain entries
        foreach (string trackerKey in keysToRemove)
        {
            if (_keychainTracker.TryRemove(trackerKey, out HashSet<string>? entries))
            {
                foreach (string keyIdentifier in entries)
                {
                    try
                    {
                        await _platformSecurityProvider.DeleteKeyFromKeychainAsync(keyIdentifier);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to cleanup keychain entry {KeyId}", keyIdentifier);
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_storageLock)
        {
            if (_disposed) return;

            // Clean up all tracked keychain entries
            foreach (KeyValuePair<string, HashSet<string>> tracker in _keychainTracker)
            {
                foreach (string keyIdentifier in tracker.Value)
                {
                    try
                    {
                        _platformSecurityProvider.DeleteKeyFromKeychainAsync(keyIdentifier).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to cleanup keychain entry {KeyId} during disposal", keyIdentifier);
                    }
                }
            }
            _keychainTracker.Clear();

            // Clean up memory cache
            _cacheLock.EnterWriteLock();
            try
            {
                foreach (KeyValuePair<uint, SodiumSecureMemoryHandle> kvp in _memoryShareCache)
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
            _disposed = true;
        }
    }
}