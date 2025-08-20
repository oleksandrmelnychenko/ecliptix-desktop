using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Network.Infrastructure;

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

        RequestInfo requestInfo = new()
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

    public void RemoveRequest(string serviceType, byte[] requestData, uint connectId)
    {
        string requestHash = ComputeRequestHash(serviceType, requestData, connectId);
        _recentRequests.TryRemove(requestHash, out _);
    }

    private static string ComputeRequestHash(string serviceType, byte[] requestData, uint connectId)
    {
        string input = $"{serviceType}:{connectId}:{Convert.ToBase64String(requestData)}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }

    private async void CleanupExpiredEntries(object? state)
    {
        try
        {
            if (!await _cleanupSemaphore.WaitAsync(0))
                return;

            try
            {
                DateTime cutoff = DateTime.UtcNow - _deduplicationWindow;
                List<string> keysToRemove = [];
                keysToRemove.AddRange(from kvp in _recentRequests where kvp.Value.Timestamp < cutoff select kvp.Key);

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
        catch (Exception)
        {
            
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
}