using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Services.Retry;

public sealed class IntelligentRetryStrategy : IRetryStrategy
{
    private static readonly TimeSpan[] BaseRetryDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(30)
    ];

    private static readonly Random SharedRandom = new();

    private const int MaxConsecutiveFailures = 5;
    private static readonly TimeSpan CircuitOpenDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MetricsRetentionPeriod = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<uint, ConnectionRetryState> _connectionStates = new();
    private readonly ConcurrentDictionary<uint, RetryMetrics> _connectionMetrics = new();
    private readonly Timer _cleanupTimer;

    public IntelligentRetryStrategy()
    {
        _cleanupTimer = new Timer(CleanupExpiredMetrics, null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 7,
        CancellationToken cancellationToken = default)
    {
        DateTime operationStart = DateTime.UtcNow;
        int attempt = 0;

        if (connectId.HasValue && !IsConnectionHealthy(connectId.Value))
        {
            ConnectionRetryState? state = GetConnectionState(connectId.Value);
            Log.Warning("Circuit breaker BLOCKED operation: {Operation} for connection {ConnectId} " +
                       "(ConsecutiveFailures: {Failures}, CircuitOpenSince: {Since})",
                operationName, connectId.Value, 
                state?.ConsecutiveFailures ?? 0,
                state?.CircuitOpenedAt?.ToString("HH:mm:ss.fff") ?? "Unknown");
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection circuit breaker is open"));
        }

        Log.Information("Starting retry operation: {Operation} (MaxRetries: {MaxRetries}){ConnHint}", 
            operationName, maxRetries, connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;
            DateTime attemptStart = DateTime.UtcNow;
            Result<TResponse, NetworkFailure> result;

            try
            {
                result = await operation();
            }
            catch (OperationCanceledException)
            {
                Log.Debug("{Operation} cancelled on attempt {Attempt}{ConnHint}", operationName, attempt,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);

                RecordFailure(connectId, operationStart);
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding("Operation cancelled"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Operation} threw exception on attempt {Attempt}{ConnHint}: {ExceptionType}",
                    operationName, attempt,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty,
                    ex.GetType().Name);

                result = Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding($"Unhandled exception: {ex.GetType().Name}: {ex.Message}"));
            }

            if (result.IsOk)
            {
                TimeSpan successAttemptDuration = DateTime.UtcNow - attemptStart;
                TimeSpan totalDuration = DateTime.UtcNow - operationStart;
                
                Log.Information("Retry operation succeeded: {Operation} on attempt {Attempt}/{MaxRetries} " +
                               "(AttemptTime: {AttemptTime}ms, TotalTime: {TotalTime}ms){ConnHint}",
                    operationName, attempt, maxRetries, (int)successAttemptDuration.TotalMilliseconds, 
                    (int)totalDuration.TotalMilliseconds,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);
                
                RecordSuccess(connectId, operationStart);
                return result;
            }

            NetworkFailure failure = result.UnwrapErr();

            if (!FailureClassification.IsTransient(failure))
            {
                Log.Debug("{Operation} not retried due to non-transient error: {Error}{ConnHint}",
                    operationName, failure.Message,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);

                RecordFailure(connectId, operationStart);
                return result;
            }

            if (attempt >= maxRetries)
            {
                TimeSpan totalDuration = DateTime.UtcNow - operationStart;
                
                Log.Warning("Retry operation exhausted: {Operation} failed after {Attempts} attempts " +
                           "(TotalTime: {TotalTime}ms) - Final error: {Error}{ConnHint}",
                    operationName, attempt, (int)totalDuration.TotalMilliseconds, failure.Message,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);

                RecordFailure(connectId, operationStart);
                UpdateCircuitBreakerState(connectId);
                return result;
            }

            TimeSpan attemptDuration = DateTime.UtcNow - attemptStart;
            TimeSpan delay = GetAdaptiveRetryDelay(attempt, connectId, failure);
            
            Log.Information("Retry attempt {Attempt}/{MaxRetries} failed: {Operation} " +
                           "(AttemptTime: {AttemptTime}ms, RetryDelay: {Delay}ms) - {Error}{ConnHint}",
                operationName, attempt, maxRetries, (int)attemptDuration.TotalMilliseconds, 
                (int)delay.TotalMilliseconds, failure.Message,
                connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                RecordFailure(connectId, operationStart);
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding("Retry cancelled"));
            }
        }
    }

    private TimeSpan GetAdaptiveRetryDelay(int attempt, uint? connectId, NetworkFailure failure)
    {
        TimeSpan baseDelay = attempt <= BaseRetryDelays.Length
            ? BaseRetryDelays[attempt - 1]
            : TimeSpan.FromSeconds(30);

        if (connectId.HasValue)
        {
            ConnectionRetryState? state = GetConnectionState(connectId.Value);
            if (state is { ConsecutiveFailures: > 2 })
            {
                double multiplier = Math.Min(state.ConsecutiveFailures * 0.5, 3.0);
                baseDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * multiplier);
            }
        }

        if (FailureClassification.IsServerShutdown(failure))
        {
            baseDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * 2.0);
        }
        else if (FailureClassification.IsChainRotationMismatch(failure))
        {
            baseDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * 0.5);
        }

        return ApplyJitter(baseDelay);
    }

    private static TimeSpan ApplyJitter(TimeSpan baseDelay)
    {
        if (baseDelay == TimeSpan.Zero)
        {
            return baseDelay;
        }

        double jitterPercent;
        lock (SharedRandom)
        {
            jitterPercent = (SharedRandom.NextDouble() - 0.5) * 0.4;
        }

        double jitteredMs = baseDelay.TotalMilliseconds * (1 + jitterPercent);
        return TimeSpan.FromMilliseconds(Math.Max(0, jitteredMs));
    }

    public void ResetConnectionState(uint? connectId = null)
    {
        if (connectId.HasValue)
        {
            _connectionStates.TryRemove(connectId.Value, out _);
            _connectionMetrics.TryRemove(connectId.Value, out _);
            Log.Debug("Reset retry strategy state for connection {ConnectId}", connectId.Value);
        }
        else
        {
            _connectionStates.Clear();
            _connectionMetrics.Clear();
            Log.Debug("Reset retry strategy state for all connections");
        }
    }

    public RetryMetrics GetRetryMetrics(uint? connectId = null)
    {
        if (connectId.HasValue && _connectionMetrics.TryGetValue(connectId.Value, out RetryMetrics? metrics))
        {
            return metrics;
        }

        if (_connectionMetrics.IsEmpty)
        {
            return new RetryMetrics(0, 0, 0, TimeSpan.Zero, DateTime.MinValue, DateTime.MinValue);
        }

        RetryMetrics[] allMetrics = _connectionMetrics.Values.ToArray();
        return new RetryMetrics(
            allMetrics.Sum(m => m.TotalAttempts),
            allMetrics.Sum(m => m.SuccessfulAttempts),
            allMetrics.Sum(m => m.FailedAttempts),
            TimeSpan.FromMilliseconds(allMetrics.Sum(m => m.TotalRetryTime.TotalMilliseconds)),
            allMetrics.Max(m => m.LastSuccessTime),
            allMetrics.Max(m => m.LastFailureTime)
        );
    }

    public ConnectionRetryState? GetConnectionState(uint connectId)
    {
        return _connectionStates.GetValueOrDefault(connectId);
    }

    public void MarkConnectionHealthy(uint connectId)
    {
        DateTime now = DateTime.UtcNow;
        _connectionStates.AddOrUpdate(connectId,
            new ConnectionRetryState(connectId, 0, now, now, false, null),
            (_, existing) => existing with
            {
                ConsecutiveFailures = 0,
                LastSuccessTime = now,
                IsCircuitOpen = false,
                CircuitOpenedAt = null
            });

        Log.Debug("Marked connection {ConnectId} as healthy", connectId);
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        if (!_connectionStates.TryGetValue(connectId, out ConnectionRetryState? state))
        {
            return true;
        }

        if (!state.IsCircuitOpen)
        {
            return true;
        }

        if (!state.CircuitOpenedAt.HasValue ||
            DateTime.UtcNow - state.CircuitOpenedAt.Value <= CircuitOpenDuration) return false;
        
        TimeSpan actualDuration = DateTime.UtcNow - state.CircuitOpenedAt.Value;
        Log.Information("Circuit breaker RESET for connection {ConnectId} after {Duration}ms " +
                       "(Previous failures: {Failures})",
            connectId, (int)actualDuration.TotalMilliseconds, state.ConsecutiveFailures);
        MarkConnectionHealthy(connectId);
        return true;
    }

    private void RecordSuccess(uint? connectId, DateTime operationStart)
    {
        TimeSpan totalTime = DateTime.UtcNow - operationStart;

        if (!connectId.HasValue) return;
        DateTime now = DateTime.UtcNow;
        _connectionMetrics.AddOrUpdate(connectId.Value,
            new RetryMetrics(1, 1, 0, totalTime, now, DateTime.MinValue),
            (_, existing) => new RetryMetrics(
                existing.TotalAttempts + 1,
                existing.SuccessfulAttempts + 1,
                existing.FailedAttempts,
                existing.TotalRetryTime.Add(totalTime),
                now,
                existing.LastFailureTime
            ));

        MarkConnectionHealthy(connectId.Value);
    }

    private void RecordFailure(uint? connectId, DateTime operationStart)
    {
        TimeSpan totalTime = DateTime.UtcNow - operationStart;

        if (!connectId.HasValue) return;
        DateTime now = DateTime.UtcNow;
        _connectionMetrics.AddOrUpdate(connectId.Value,
            new RetryMetrics(1, 0, 1, totalTime, DateTime.MinValue, now),
            (_, existing) => new RetryMetrics(
                existing.TotalAttempts + 1,
                existing.SuccessfulAttempts,
                existing.FailedAttempts + 1,
                existing.TotalRetryTime.Add(totalTime),
                existing.LastSuccessTime,
                now
            ));

        _connectionStates.AddOrUpdate(connectId.Value,
            new ConnectionRetryState(connectId.Value, 1, now, null, false, null),
            (_, existing) => existing with
            {
                ConsecutiveFailures = existing.ConsecutiveFailures + 1,
                LastFailureTime = now
            });
    }

    private void UpdateCircuitBreakerState(uint? connectId)
    {
        if (!connectId.HasValue) return;

        if (!_connectionStates.TryGetValue(connectId.Value, out ConnectionRetryState? state) ||
            state.ConsecutiveFailures < MaxConsecutiveFailures) return;
        DateTime now = DateTime.UtcNow;
        _connectionStates.TryUpdate(connectId.Value,
            state with { IsCircuitOpen = true, CircuitOpenedAt = now },
            state);

        Log.Warning("Circuit breaker opened for connection {ConnectId} after {ConsecutiveFailures} failures",
            connectId.Value, state.ConsecutiveFailures);
    }

    private void CleanupExpiredMetrics(object? state)
    {
        DateTime cutoff = DateTime.UtcNow - MetricsRetentionPeriod;
        uint[] expiredConnections = _connectionMetrics
            .Where(kvp => kvp.Value.LastSuccessTime < cutoff && kvp.Value.LastFailureTime < cutoff)
            .Select(kvp => kvp.Key)
            .ToArray();

        foreach (uint connectId in expiredConnections)
        {
            _connectionMetrics.TryRemove(connectId, out _);
            _connectionStates.TryRemove(connectId, out _);
        }

        if (expiredConnections.Length > 0)
        {
            Log.Debug("Cleaned up metrics for {Count} expired connections", expiredConnections.Length);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}