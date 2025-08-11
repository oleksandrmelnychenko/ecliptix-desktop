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
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);
    private static readonly Random SharedRandom = new();

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 3,
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
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding("Operation cancelled"));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "{Operation} threw exception on attempt {Attempt}{ConnHint}", operationName, attempt,
                    connectId.HasValue ? $" (ConnectId: {connectId.Value})" : string.Empty);
                result = Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding($"Unhandled exception: {ex.Message}"));
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

            TimeSpan delay = ComputeBackoffWithJitter(attempt);
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

    private static TimeSpan ComputeBackoffWithJitter(int attempt)
    {
        double exp = Math.Min(Math.Pow(2, attempt - 1), MaxDelay.TotalMilliseconds / BaseDelay.TotalMilliseconds);
        int baseMs = (int)(BaseDelay.TotalMilliseconds * exp);

        int jitterMs;
        lock (SharedRandom)
        {
            jitterMs = SharedRandom.Next(0, baseMs + 1);
        }

        int finalMs = Math.Min(baseMs + jitterMs, (int)MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(finalMs);
    }
}