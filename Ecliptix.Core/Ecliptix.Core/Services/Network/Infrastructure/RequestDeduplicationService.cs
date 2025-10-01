using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Utilities;

namespace Ecliptix.Core.Services.Network.Infrastructure;

public class RequestDeduplicationService : IDisposable
{
    private readonly ExpiringCache<string, RequestInfo> _recentRequests;

    public RequestDeduplicationService(
        TimeSpan deduplicationWindow)
    {
        _recentRequests = new ExpiringCache<string, RequestInfo>(deduplicationWindow);
    }

    public Task<bool> IsDuplicateRequestAsync(
        string serviceType,
        byte[] requestData,
        uint connectId)
    {
        string requestHash = ComputeRequestHash(serviceType, requestData, connectId);

        if (_recentRequests.TryGetValue(requestHash, out RequestInfo? existingRequest))
        {
            return Task.FromResult(true);
        }

        RequestInfo requestInfo = new()
        {
            ServiceType = serviceType,
            ConnectId = connectId,
            Timestamp = DateTime.UtcNow,
            RequestCount = 1
        };

        _recentRequests.AddOrUpdate(
            requestHash,
            requestInfo,
            existing =>
            {
                existing.RequestCount++;
                return existing;
            });

        if (requestInfo.RequestCount > 3)
        {
            // TODO: Implement rate limiting logic
        }

        return Task.FromResult(false);
    }

    public void RemoveRequest(string serviceType, byte[] requestData, uint connectId)
    {
        string requestHash = ComputeRequestHash(serviceType, requestData, connectId);
        _recentRequests.TryRemove(requestHash);
    }

    private static string ComputeRequestHash(string serviceType, byte[] requestData, uint connectId)
    {
        string input = $"{serviceType}:{connectId}:{Convert.ToBase64String(requestData)}";
        byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashBytes);
    }

    public void Dispose()
    {
        _recentRequests?.Dispose();
    }

    private class RequestInfo
    {
        public string ServiceType { get; set; } = string.Empty;
        public uint ConnectId { get; set; }
        public DateTime Timestamp { get; set; }
        public int RequestCount { get; set; }
    }
}