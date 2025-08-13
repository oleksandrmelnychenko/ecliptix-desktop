using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Network.Services;

public class RequestDeduplicationService : IDisposable
{
    private readonly ConcurrentDictionary<string, RequestInfo> _recentRequests;
    private readonly TimeSpan _deduplicationWindow;
    private readonly Timer _cleanupTimer;
    private readonly SemaphoreSlim _cleanupSemaphore;

    public RequestDeduplicationService(
        TimeSpan deduplicationWindow)
    {
        _deduplicationWindow = deduplicationWindow;
        _recentRequests = new ConcurrentDictionary<string, RequestInfo>();
        _cleanupSemaphore = new SemaphoreSlim(1, 1);
        
        _cleanupTimer = new Timer(
            CleanupExpiredEntries,
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    public Task<bool> IsDuplicateRequestAsync(
        string serviceType,
        byte[] requestData,
        uint connectId)
    {
        string requestHash = ComputeRequestHash(serviceType, requestData, connectId);
        DateTime now = DateTime.UtcNow;
        
        if (_recentRequests.TryGetValue(requestHash, out RequestInfo? existingRequest))
        {
            if (now - existingRequest.Timestamp < _deduplicationWindow)
            {
                return Task.FromResult(true);
            }
        }
        
        RequestInfo requestInfo = new RequestInfo
        {
            ServiceType = serviceType,
            ConnectId = connectId,
            Timestamp = now,
            RequestCount = 1
        };
        
        _recentRequests.AddOrUpdate(
            requestHash,
            requestInfo,
            (key, existing) =>
            {
                existing.RequestCount++;
                existing.Timestamp = now;
                return existing;
            });
        
        if (requestInfo.RequestCount > 3)
        {
        }
        
        return Task.FromResult(false);
    }

    public RequestStatistics GetStatistics()
    {
        RequestStatistics stats = new RequestStatistics
        {
            TotalRequests = _recentRequests.Count,
            DuplicatesDetected = 0,
            UniqueServiceTypes = new HashSet<string>()
        };
        
        foreach (RequestInfo request in _recentRequests.Values)
        {
            if (request.RequestCount > 1)
                stats.DuplicatesDetected += request.RequestCount - 1;
            stats.UniqueServiceTypes.Add(request.ServiceType);
        }
        
        return stats;
    }

    private string ComputeRequestHash(string serviceType, byte[] requestData, uint connectId)
    {
        using SHA256 sha256 = SHA256.Create();
        string input = $"{serviceType}:{connectId}:{Convert.ToBase64String(requestData)}";
        byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }

    private async void CleanupExpiredEntries(object? state)
    {
        if (!await _cleanupSemaphore.WaitAsync(0))
            return;
        
        try
        {
            DateTime cutoff = DateTime.UtcNow - _deduplicationWindow;
            List<string> keysToRemove = new List<string>();
            
            foreach (KeyValuePair<string, RequestInfo> kvp in _recentRequests)
            {
                if (kvp.Value.Timestamp < cutoff)
                    keysToRemove.Add(kvp.Key);
            }
            
            foreach (string key in keysToRemove)
            {
                _recentRequests.TryRemove(key, out _);
            }
            
            if (keysToRemove.Count > 0)
            {
            }
        }
        finally
        {
            _cleanupSemaphore.Release();
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cleanupSemaphore?.Dispose();
    }

    private class RequestInfo
    {
        public string ServiceType { get; set; } = string.Empty;
        public uint ConnectId { get; set; }
        public DateTime Timestamp { get; set; }
        public int RequestCount { get; set; }
    }

    public class RequestStatistics
    {
        public int TotalRequests { get; set; }
        public int DuplicatesDetected { get; set; }
        public HashSet<string> UniqueServiceTypes { get; set; } = new();
    }
}