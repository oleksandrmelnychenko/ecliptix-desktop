using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IRetryStrategy : IDisposable
{
    Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int? maxRetries = null,
        CancellationToken cancellationToken = default);

    Task<Result<TResponse, NetworkFailure>> ExecuteManualRetryOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int? maxRetries = null,
        CancellationToken cancellationToken = default);

    void ResetConnectionState(uint? connectId = null);

    void MarkConnectionHealthy(uint connectId);

    bool HasExhaustedOperations();

    void ClearExhaustedOperations();
}