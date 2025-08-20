using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;

namespace Ecliptix.Core.Core.Communication;

public interface IModuleSharedState
{
    T? Get<T>(string key);
    void Set<T>(string key, T value, TimeSpan? expiration = null);
    bool Remove(string key);
    void Clear();
}

public class ModuleSharedState : IModuleSharedState, IDisposable
{
    private readonly ConcurrentDictionary<string, StateEntry> _state = new();
    private readonly Timer _cleanupTimer;

    public ModuleSharedState()
    {
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public T? Get<T>(string key)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (_state.TryGetValue(key, out StateEntry? entry))
        {
            if (entry.IsExpired)
            {
                _state.TryRemove(key, out _);
                return default;
            }

            entry.LastAccessed = DateTime.UtcNow;

            if (entry.Value is T typedValue)
            {
                return typedValue;
            }
        }

        return default;
    }

    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be null or empty", nameof(key));

        DateTime now = DateTime.UtcNow;

        StateEntry newEntry = new()
        {
            Value = value,
            Type = typeof(T),
            CreatedAt = now,
            LastAccessed = now,
            ExpiresAt = expiration.HasValue ? now.Add(expiration.Value) : null
        };

        _state[key] = newEntry;

        Log.Debug("Set shared state value for key {Key} with type {Type}", key, typeof(T).Name);
    }
    public bool Remove(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;

        if (_state.TryRemove(key, out StateEntry? _))
        {
            Log.Debug("Removed shared state value for key {Key}", key);
            return true;
        }

        return false;
    }

    public void Clear()
    {
        _state.Clear();
        Log.Information("Cleared all shared state");
    }

    private void CleanupExpiredEntries(object? state)
    {
        List<string> expiredKeys = [];
        expiredKeys.AddRange(from kvp in _state where kvp.Value.IsExpired select kvp.Key);

        foreach (string key in expiredKeys.Where(key => _state.TryRemove(key, out StateEntry? _)))
        {
            Log.Debug("Cleaned up expired shared state entry for key {Key}", key);
        }

        if (expiredKeys.Count > 0)
        {
            Log.Debug("Cleaned up {Count} expired shared state entries", expiredKeys.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
        _state.Clear();
    }
}

internal class StateEntry
{
    public object? Value { get; set; }
    public Type Type { get; set; } = typeof(object);
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
}
