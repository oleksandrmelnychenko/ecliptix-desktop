namespace Ecliptix.Network.Services.Network.Infrastructure;

public interface IPendingRequestManager
{
    void RegisterPendingRequest(string requestId, Func<CancellationToken, Task> retryAction);
    void RemovePendingRequest(string requestId);
    Task<int> RetryAllPendingRequestsAsync(CancellationToken cancellationToken = default);
}
