using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public record RetryMetrics(
    int TotalAttempts,
    int SuccessfulAttempts,
    int FailedAttempts,
    TimeSpan TotalRetryTime,
    DateTime LastSuccessTime,
    DateTime LastFailureTime);

public record ConnectionRetryState(
    uint ConnectId,
    int ConsecutiveFailures,
    DateTime LastFailureTime,
    DateTime? LastSuccessTime,
    bool IsCircuitOpen,
    DateTime? CircuitOpenedAt);

public interface IRetryStrategy
{
    Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 7, 
        CancellationToken cancellationToken = default);
        
    void ResetConnectionState(uint? connectId = null);
    
    RetryMetrics GetRetryMetrics(uint? connectId = null);
    
    ConnectionRetryState? GetConnectionState(uint connectId);
    
    void MarkConnectionHealthy(uint connectId);
    
    bool IsConnectionHealthy(uint connectId);
}