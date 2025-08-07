using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IRetryStrategy
{
    Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default);
}