using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Utilities;

public sealed class ExpiringCache<TKey, TValue> : IDisposable
    where TKey : notnull
    where TValue : notnull
{
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();
    private readonly TimeSpan _expirationWindow;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);
    private readonly Action<int>? _onCleanup;

    public int Count => _cache.Count;

    public ExpiringCache(TimeSpan expirationWindow, TimeSpan? cleanupInterval = null, Action<int>? onCleanup = null)
    {
        _expirationWindow = expirationWindow;
        _onCleanup = onCleanup;

        TimeSpan interval = cleanupInterval ?? TimeSpan.FromMinutes(1);
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, interval, interval);
    }

    public TValue AddOrUpdate(TKey key, TValue value, Func<TValue, TValue>? updateExisting = null)
    {
        CacheEntry entry = _cache.AddOrUpdate(
            key,
            _ => new CacheEntry { Value = value, Timestamp = DateTime.UtcNow },
            (_, existing) =>
            {
                existing.Value = updateExisting != null ? updateExisting(existing.Value) : value;
                existing.Timestamp = DateTime.UtcNow;
                return existing;
            });

        return entry.Value;
    }

    /// <summary>
    /// Tries to get a value from the cache if it exists and hasn't expired.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The value if found and not expired, otherwise default.</param>
    /// <returns>True if the value was found and not expired, otherwise false.</returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (_cache.TryGetValue(key, out CacheEntry? entry))
        {
            if (DateTime.UtcNow - entry.Timestamp < _expirationWindow)
            {
                value = entry.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Checks if a key exists in the cache and hasn't expired.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key exists and hasn't expired, otherwise false.</returns>
    public bool Contains(TKey key)
    {
        if (_cache.TryGetValue(key, out CacheEntry? entry))
        {
            return DateTime.UtcNow - entry.Timestamp < _expirationWindow;
        }
        return false;
    }

    /// <summary>
    /// Removes an entry from the cache.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if the key was found and removed, otherwise false.</returns>
    public bool TryRemove(TKey key)
    {
        return _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes an entry from the cache and returns its value.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <param name="value">The value that was removed, or default if not found.</param>
    /// <returns>True if the key was found and removed, otherwise false.</returns>
    public bool TryRemove(TKey key, out TValue value)
    {
        if (_cache.TryRemove(key, out CacheEntry? entry))
        {
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>
    /// Clears all entries from the cache.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
    }

    /// <summary>
    /// Gets all non-expired keys currently in the cache.
    /// </summary>
    /// <returns>Collection of non-expired keys.</returns>
    public IEnumerable<TKey> GetKeys()
    {
        DateTime now = DateTime.UtcNow;
        return _cache
            .Where(kvp => now - kvp.Value.Timestamp < _expirationWindow)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    /// <summary>
    /// Manually triggers cleanup of expired entries.
    /// </summary>
    /// <returns>Number of entries removed.</returns>
    public async Task<int> CleanupAsync()
    {
        if (!await _cleanupSemaphore.WaitAsync(0))
            return 0;

        try
        {
            DateTime cutoff = DateTime.UtcNow - _expirationWindow;
            List<TKey> keysToRemove = _cache
                .Where(kvp => kvp.Value.Timestamp < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (TKey key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _onCleanup?.Invoke(keysToRemove.Count);
            }

            return keysToRemove.Count;
        }
        finally
        {
            _cleanupSemaphore.Release();
        }
    }

    private async void CleanupExpiredEntries(object? state)
    {
        try
        {
            await CleanupAsync();
        }
        catch (Exception)
        {
            // Suppress exceptions in timer callback to prevent app crash
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cleanupSemaphore?.Dispose();
    }

    private sealed class CacheEntry
    {
        public TValue Value { get; set; } = default!;
        public DateTime Timestamp { get; set; }
    }
}
