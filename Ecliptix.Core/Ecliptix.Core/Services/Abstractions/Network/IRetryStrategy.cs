using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface IRetryStrategy : IDisposable
{
    Task<Result<TResponse, NetworkFailure>> ExecuteRpcOperationAsync<TResponse>(
        Func<CancellationToken, Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint connectId,
        int? maxRetries = null,
        CancellationToken cancellationToken = default);

    Task<Result<TResponse, NetworkFailure>> ExecuteManualRetryRpcOperationAsync<TResponse>(
        Func<CancellationToken, Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint connectId,
        int? maxRetries = null,
        CancellationToken cancellationToken = default);

    void MarkConnectionHealthy(uint connectId);

    void ClearExhaustedOperations();
}
