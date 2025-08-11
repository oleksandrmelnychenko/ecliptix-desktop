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
    private static readonly TimeSpan[] RetryDelays =
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

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 7,
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

    private static TimeSpan GetRetryDelayWithJitter(int attempt)
    {
        TimeSpan baseDelay = attempt <= RetryDelays.Length
            ? RetryDelays[attempt - 1]
            : TimeSpan.FromSeconds(30);

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
}