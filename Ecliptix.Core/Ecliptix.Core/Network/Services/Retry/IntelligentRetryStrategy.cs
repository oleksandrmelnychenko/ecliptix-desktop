using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Services.Retry;

public sealed class IntelligentRetryStrategy : IRetryStrategy
{
    // Messaging app-inspired retry strategy similar to Telegram, Signal, Viber
    private static readonly TimeSpan[] RetryDelays = 
    {
        TimeSpan.Zero,                    // 1st retry: immediate (0ms)
        TimeSpan.FromSeconds(1),          // 2nd retry: 1 second
        TimeSpan.FromSeconds(2),          // 3rd retry: 2 seconds  
        TimeSpan.FromSeconds(5),          // 4th retry: 5 seconds
        TimeSpan.FromSeconds(10),         // 5th retry: 10 seconds
        TimeSpan.FromSeconds(20),         // 6th retry: 20 seconds
        TimeSpan.FromSeconds(30)          // 7th retry: 30 seconds
    };
    
    private static readonly Random SharedRandom = new();

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 7,  // Increased to match messaging app behavior
        CancellationToken cancellationToken = default)
    {
        int attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            attempt++;
            Result<TResponse, NetworkFailure> result;
            try
            {
                result = await operation();
            }
            catch (OperationCanceledException)
            {
                Log.Debug("{Operation} cancelled on attempt {Attempt}{ConnHint}", operationName, attempt,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);
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
                return result;
            }

            NetworkFailure failure = result.UnwrapErr();

            if (!FailureClassification.IsTransient(failure))
            {
                Log.Debug("{Operation} not retried due to non-transient error: {Error}{ConnHint}",
                    operationName, failure.Message,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);
                return result;
            }

            if (attempt >= maxRetries)
            {
                Log.Warning("{Operation} failed after {Attempts} attempts: {Error}{ConnHint}",
                    operationName, attempt, failure.Message,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);
                return result;
            }

            TimeSpan delay = GetRetryDelayWithJitter(attempt);
            Log.Debug("{Operation} transient error, retrying attempt {Attempt} after {Delay} ms: {Error}{ConnHint}",
                operationName, attempt + 1, (int)delay.TotalMilliseconds, failure.Message,
                connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding("Retry cancelled"));
            }
        }
    }

    /// <summary>
    /// Gets retry delay based on messaging app patterns (Telegram, Signal, Viber)
    /// with added jitter to prevent thundering herd effect
    /// </summary>
    private static TimeSpan GetRetryDelayWithJitter(int attempt)
    {
        // Use predefined delays for first attempts, then fallback to exponential backoff
        TimeSpan baseDelay = attempt <= RetryDelays.Length 
            ? RetryDelays[attempt - 1] 
            : TimeSpan.FromSeconds(30); // Cap at 30 seconds for subsequent attempts

        // Skip jitter for immediate retry (first attempt)
        if (baseDelay == TimeSpan.Zero)
        {
            return baseDelay;
        }

        // Add ±20% jitter to prevent thundering herd
        double jitterPercent;
        lock (SharedRandom)
        {
            jitterPercent = (SharedRandom.NextDouble() - 0.5) * 0.4; // -0.2 to +0.2 (±20%)
        }

        double jitteredMs = baseDelay.TotalMilliseconds * (1 + jitterPercent);
        return TimeSpan.FromMilliseconds(Math.Max(0, jitteredMs));
    }
}