using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

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

    public bool TryRemove(TKey key) => _cache.TryRemove(key, out _);

    public void Clear() => _cache.Clear();

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
        catch (Exception ex)
        {
            Log.Error(ex, "ExpiringCache cleanup failed");
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
