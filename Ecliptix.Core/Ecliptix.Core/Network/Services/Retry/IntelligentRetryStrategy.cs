using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Services.Retry;

public class IntelligentRetryStrategy : IRetryStrategy
{
    private readonly ConcurrentDictionary<string, CircuitBreakerState> _circuitBreakerStates = new();

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

    private Func<int, TimeSpan> ContextAwareBackoff(NetworkFailure? lastFailure = null, uint? connectId = null)
    {
        double baseMultiplier = CategorizeFailureForRetry(lastFailure);
        TimeSpan baseDelay = TimeSpan.FromSeconds(2 * baseMultiplier);
        TimeSpan maxDelay = TimeSpan.FromMinutes(3 * baseMultiplier);

        string circuitKey = $"{connectId}_{lastFailure?.FailureType}";
        return IsCircuitBreakerOpen(circuitKey) ? _ => TimeSpan.FromMinutes(5) : IntelligentBackoff(baseDelay, 2.0, maxDelay);
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 10,
        CancellationToken cancellationToken = default)
    {
        string circuitKey = $"SecrecyChannel_{connectId}_{operationName}";
        Exception? lastException = null;
        NetworkFailure? lastFailure = null;
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            if (IsCircuitBreakerOpen(circuitKey))
            {
                Log.Warning("Circuit breaker is open for {Operation}, skipping attempt {Attempt}", 
                    operationName, attempt);
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                continue;
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
                
                if (IsServerDownFailure(lastFailure))
                {
                    Log.Warning("Server down detected for {Operation} on attempt {Attempt}/{MaxRetries}, will retry: {Error}", 
                        operationName, attempt, maxRetries, lastFailure.Message);
                }
                
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
                    await Task.Delay(delay, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log.Information("Secrecy channel operation {Operation} cancelled during attempt {Attempt}", 
                    operationName, attempt);
                throw;
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                lastException = ex;
                TimeSpan delay = ContextAwareBackoff(lastFailure, connectId)(attempt);
                Log.Warning(ex, "Secrecy channel operation {Operation} threw exception on attempt {Attempt}/{MaxRetries}, retrying in {Delay}", 
                    operationName, attempt, maxRetries, delay);
                await Task.Delay(delay, cancellationToken);
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
        return failure.FailureType switch
        {
            NetworkFailureType.DataCenterShutdown or NetworkFailureType.DataCenterNotResponding => true,
            NetworkFailureType.InvalidRequestType => IsTransientNetworkIssue(failure.Message),
            NetworkFailureType.EcliptixProtocolFailure => IsRecoverableProtocolFailure(failure.Message),
            _ => false
        };
    }

    private static bool IsTransientNetworkIssue(string message)
    {
        string lowerMessage = message.ToLowerInvariant();
        
        if (lowerMessage.Contains("network") || lowerMessage.Contains("connection") ||
            lowerMessage.Contains("unreachable") || lowerMessage.Contains("timeout") ||
            lowerMessage.Contains("deadline") || lowerMessage.Contains("unavailable") ||
            lowerMessage.Contains("connection refused") || lowerMessage.Contains("subchannel"))
            return true;
            
        if (lowerMessage.Contains("server error") || lowerMessage.Contains("internal server") ||
            lowerMessage.Contains("service unavailable") || lowerMessage.Contains("502") ||
            lowerMessage.Contains("503") || lowerMessage.Contains("504"))
            return true;
            
        return false;
    }
    
    private static bool IsRecoverableProtocolFailure(string message)
    {
        string lowerMessage = message.ToLowerInvariant();
        
        if (lowerMessage.Contains("desync") || lowerMessage.Contains("decrypt") ||
            lowerMessage.Contains("rekey") || lowerMessage.Contains("chain"))
            return true;
            
        if (lowerMessage.Contains("auth") || lowerMessage.Contains("unauthorized") ||
            lowerMessage.Contains("forbidden") || lowerMessage.Contains("bad request") ||
            lowerMessage.Contains("invalid") || lowerMessage.Contains("validation"))
        {
        }

        return false; 
    }
    
    private static bool IsServerDownFailure(NetworkFailure failure)
    {
        if (failure.FailureType == NetworkFailureType.DataCenterShutdown)
            return true;
            
        if (failure.FailureType == NetworkFailureType.DataCenterNotResponding)
        {
            string message = failure.Message.ToLowerInvariant();
            return message.Contains("server shutdown") || 
                   message.Contains("server unavailable") ||
                   message.Contains("service unavailable") ||
                   message.Contains("connection refused") ||
                   message.Contains("host unreachable") ||
                   message.Contains("statuscode=\"unavailable\"") || 
                   message.Contains("error connecting to subchannel"); 
        }
        
        return false;
    }


    private static double CategorizeFailureForRetry(NetworkFailure? failure)
    {
        if (failure == null) return 1.0;

        string message = failure.Message.ToLowerInvariant();

        if (IsServerDownFailure(new NetworkFailure(NetworkFailureType.DataCenterNotResponding, message ?? "")))
        {
            return 5.0;
        }
        
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

    private bool IsCircuitBreakerOpen(string circuitKey)
    {
        if (!_circuitBreakerStates.TryGetValue(circuitKey, out CircuitBreakerState? state)) return false;
        DateTime now = DateTime.UtcNow;
            
        if (now - state.LastFailureTime < TimeSpan.FromMinutes(5))
        {
            return true;
        }
            
        CircuitBreakerState updatedState = state with { LastFailureTime = DateTime.MinValue };
        _circuitBreakerStates.TryUpdate(circuitKey, updatedState, state);

        return false;
    }

    private void UpdateCircuitBreakerState(string circuitKey, bool success)
    {
        DateTime now = DateTime.UtcNow;
        
        if (success)
        {
            _circuitBreakerStates.AddOrUpdate(
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
            _circuitBreakerStates.AddOrUpdate(
                circuitKey,
                new CircuitBreakerState { LastFailureTime = now, FailureCount = 1 },
                (_, existingState) => existingState with 
                { 
                    LastFailureTime = now, 
                    FailureCount = existingState.FailureCount + 1 
                });
        }
    }
}

internal record CircuitBreakerState
{
    public DateTime LastFailureTime { get; init; } = DateTime.MinValue;
    public DateTime LastSuccessTime { get; init; } = DateTime.MinValue;
    public int FailureCount { get; init; } = 0;
}