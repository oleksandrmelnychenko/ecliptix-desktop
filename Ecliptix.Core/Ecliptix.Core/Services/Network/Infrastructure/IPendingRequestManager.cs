using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Network.Infrastructure;

public interface IPendingRequestManager
{
    void RegisterPendingRequest(string requestId, Func<CancellationToken, Task> retryAction);
    void RemovePendingRequest(string requestId);
    Task<int> RetryAllPendingRequestsAsync(CancellationToken cancellationToken = default);
}
