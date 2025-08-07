using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Advanced;

public static class IntelligentRetryStrategy
{
    private static readonly ConcurrentDictionary<string, CircuitBreakerState> CircuitBreakerStates = new();

    private static Func<int, TimeSpan> IntelligentBackoff(
        TimeSpan baseDelay = default,
        double multiplier = 2.0,
        TimeSpan maxDelay = default,
        double jitterFactor = 0.1)
    {
        if (baseDelay == TimeSpan.Zero) baseDelay = TimeSpan.FromSeconds(1);
        if (maxDelay == TimeSpan.Zero) maxDelay = TimeSpan.FromMinutes(5);

        return attempt =>
        {
            TimeSpan delay = TimeSpan.FromMilliseconds(
                baseDelay.TotalMilliseconds * Math.Pow(multiplier, attempt - 1));

            double jitter = delay.TotalMilliseconds * jitterFactor * Random.Shared.NextDouble();
            delay = delay.Add(TimeSpan.FromMilliseconds(jitter));

            return delay > maxDelay ? maxDelay : delay;
        };
    }

    private static Func<int, TimeSpan> ContextAwareBackoff(NetworkFailure? lastFailure = null, uint? connectId = null)
    {
        double baseMultiplier = CategorizeFailureForRetry(lastFailure);
        TimeSpan baseDelay = TimeSpan.FromSeconds(2 * baseMultiplier);
        TimeSpan maxDelay = TimeSpan.FromMinutes(3 * baseMultiplier);

        string circuitKey = $"{connectId}_{lastFailure?.FailureType}";
        if (IsCircuitBreakerOpen(circuitKey))
        {
            return _ => TimeSpan.FromMinutes(5);
        }

        return IntelligentBackoff(baseDelay, 2.0, maxDelay);
    }

    public static async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 3)
    {
        string circuitKey = $"SecrecyChannel_{connectId}_{operationName}";
        Exception? lastException = null;
        NetworkFailure? lastFailure = null;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            if (IsCircuitBreakerOpen(circuitKey))
            {
                Log.Warning("Circuit breaker is open for {Operation}, skipping attempt {Attempt}", 
                    operationName, attempt);
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
            
            try
            {
                Log.Debug("Executing secrecy channel operation {Operation}, attempt {Attempt}/{MaxRetries}", 
                    operationName, attempt, maxRetries);
                
                Result<TResponse, NetworkFailure> result = await operation();
                
                if (result.IsOk)
                {
                    Log.Debug("Secrecy channel operation {Operation} succeeded on attempt {Attempt}", 
                        operationName, attempt);
                    UpdateCircuitBreakerState(circuitKey, true);
                    return result;
                }
                
                lastFailure = result.UnwrapErr();
                
                if (!ShouldRetryFailure(lastFailure))
                {
                    Log.Warning("Secrecy channel operation {Operation} failed with non-retryable error: {Error}", 
                        operationName, lastFailure.Message);
                    UpdateCircuitBreakerState(circuitKey, false);
                    return result;
                }
                
                if (attempt < maxRetries)
                {
                    TimeSpan delay = ContextAwareBackoff(lastFailure, connectId)(attempt);
                    Log.Warning("Secrecy channel operation {Operation} failed on attempt {Attempt}/{MaxRetries}, retrying in {Delay}: {Error}", 
                        operationName, attempt, maxRetries, delay, lastFailure.Message);
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                TimeSpan delay = ContextAwareBackoff(lastFailure, connectId)(attempt);
                Log.Warning(ex, "Secrecy channel operation {Operation} threw exception on attempt {Attempt}/{MaxRetries}, retrying in {Delay}", 
                    operationName, attempt, maxRetries, delay);
                await Task.Delay(delay);
            }
        }
        
        UpdateCircuitBreakerState(circuitKey, false);
        
        if (lastFailure != null)
        {
            Log.Error("Secrecy channel operation {Operation} failed after {MaxRetries} attempts: {Error}", 
                operationName, maxRetries, lastFailure.Message);
            return Result<TResponse, NetworkFailure>.Err(lastFailure);
        }
        
        if (lastException != null)
        {
            Log.Error(lastException, "Secrecy channel operation {Operation} failed after {MaxRetries} attempts", 
                operationName, maxRetries);
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(lastException.Message, lastException));
        }
        
        return Result<TResponse, NetworkFailure>.Err(
            NetworkFailure.DataCenterNotResponding($"Operation {operationName} failed after {maxRetries} attempts"));
    }

    private static bool ShouldRetryFailure(NetworkFailure failure)
    {
        string message = failure.Message.ToLowerInvariant();

        if (message.Contains("network") || message.Contains("connection") || 
            message.Contains("unreachable") || message.Contains("timeout"))
            return true;

        if (message.Contains("server") || message.Contains("internal") || 
            message.Contains("service unavailable"))
            return true;

        if (message.Contains("auth") || message.Contains("unauthorized") || 
            message.Contains("forbidden"))
            return false;

        if (message.Contains("bad request") || message.Contains("invalid"))
            return false;

        if (message.Contains("desync") || message.Contains("decrypt") || message.Contains("rekey"))
            return true;

        return true;
    }

    public static Func<int, TResult, Task> CreateIntelligentRetryCallback<TResult>(
        string operationName,
        uint? connectId = null)
    {
        return async (attempt, result) =>
        {
            string errorMessage = ExtractErrorMessage(result);
            
            Log.Warning("Advanced retry - Operation {Operation} failed on attempt {Attempt} for connection {ConnectId}: {Error}", 
                operationName, attempt, connectId ?? 0, errorMessage);

            if (attempt >= 3)
            {
                string circuitKey = $"{connectId}_{operationName}";
                UpdateCircuitBreakerState(circuitKey, false);
            }

            await Task.CompletedTask;
        };
    }

    private static string ExtractErrorMessage<TResult>(TResult result)
    {
        return result switch
        {
            Result<Unit, NetworkFailure> r when r.IsErr => r.UnwrapErr().Message,
            Result<bool, NetworkFailure> r when r.IsErr => r.UnwrapErr().Message,
            _ => "Unknown error"
        };
    }

    private static double CategorizeFailureForRetry(NetworkFailure? failure)
    {
        if (failure == null) return 1.0;

        string message = failure.Message.ToLowerInvariant();

        return message switch
        {
            not null when message.Contains("rate") || message.Contains("limit") => 3.0,
            not null when message.Contains("network") || message.Contains("connection") => 2.0,
            not null when message.Contains("timeout") => 1.5,
            not null when message.Contains("server") => 1.8,
            not null when message.Contains("desync") || message.Contains("decrypt") => 2.5,
            _ => 1.0
        };
    }

    private static bool IsCircuitBreakerOpen(string circuitKey)
    {
        if (CircuitBreakerStates.TryGetValue(circuitKey, out CircuitBreakerState? state))
        {
            DateTime now = DateTime.UtcNow;
            
            if (now - state.LastFailureTime < TimeSpan.FromMinutes(5))
            {
                return true;
            }
            
            CircuitBreakerState updatedState = state with { LastFailureTime = DateTime.MinValue };
            CircuitBreakerStates.TryUpdate(circuitKey, updatedState, state);
        }
        
        return false;
    }

    private static void UpdateCircuitBreakerState(string circuitKey, bool success)
    {
        DateTime now = DateTime.UtcNow;
        
        if (success)
        {
            CircuitBreakerStates.AddOrUpdate(
                circuitKey,
                new CircuitBreakerState { LastFailureTime = DateTime.MinValue, FailureCount = 0 },
                (_, existingState) => existingState with 
                { 
                    LastFailureTime = DateTime.MinValue, 
                    FailureCount = 0,
                    LastSuccessTime = now 
                });
        }
        else
        {
            CircuitBreakerStates.AddOrUpdate(
                circuitKey,
                new CircuitBreakerState { LastFailureTime = now, FailureCount = 1 },
                (_, existingState) => existingState with 
                { 
                    LastFailureTime = now, 
                    FailureCount = existingState.FailureCount + 1 
                });
        }
    }
    
    public static void RecordOperationSuccess(string operationName, uint? connectId = null)
    {
        string circuitKey = $"{connectId}_{operationName}";
        UpdateCircuitBreakerState(circuitKey, true);
    }
}

internal record CircuitBreakerState
{
    public DateTime LastFailureTime { get; init; } = DateTime.MinValue;
    public DateTime LastSuccessTime { get; init; } = DateTime.MinValue;
    public int FailureCount { get; init; } = 0;
}