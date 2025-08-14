using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Services.Retry;

public class SecrecyChannelRetryStrategy : IRetryStrategy, IDisposable
{
    private readonly ImprovedRetryConfiguration _configuration;
    private readonly IAsyncPolicy<object> _retryPolicy;
    private readonly INetworkEvents _networkEvents;
    private Lazy<NetworkProvider>? _lazyNetworkProvider;
    private readonly ConcurrentDictionary<string, RetryOperationInfo> _activeRetryOperations = new();

    private class RetryOperationInfo
    {
        public string OperationName { get; set; } = string.Empty;
        public uint ConnectId { get; set; }
        public DateTime StartTime { get; set; }
        public int CurrentRetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string UniqueKey { get; set; } = string.Empty;
        public bool IsExhausted { get; set; }
        public Func<Task<Result<object, NetworkFailure>>>? Operation { get; set; }
    }

    public SecrecyChannelRetryStrategy(
        IConfiguration configuration,
        INetworkEvents networkEvents)
    {
        _configuration = GetRetryConfiguration(configuration);
        _networkEvents = networkEvents;
        _retryPolicy = CreateRetryPolicy();
        
        // Listen for manual retry requests
        _networkEvents.ManualRetryRequested
            .Subscribe(async evt => await HandleManualRetryRequestAsync(evt));
    }

    private async Task HandleManualRetryRequestAsync(ManualRetryRequestedEvent evt)
    {
        Log.Information("🔄 MANUAL RETRY REQUEST: Received manual retry request for ConnectId {ConnectId}", evt.ConnectId);
        
        bool anySucceeded = await RetryAllExhaustedOperationsAsync();
        
        // If no operations succeeded, they remain exhausted and button should reappear after a small delay
        if (!anySucceeded && AreAllRetryOperationsExhausted())
        {
            Log.Warning("🔄 MANUAL RETRY: All operations still exhausted after manual retry. Button will reappear");
            
            // Give a short delay before showing the button again
            await Task.Delay(TimeSpan.FromSeconds(2));
            
            _networkEvents.InitiateChangeState(
                NetworkStatusChangedEvent.New(NetworkStatus.RetriesExhausted));
        }
        else if (anySucceeded)
        {
            Log.Information("🔄 MANUAL RETRY: Some operations succeeded. Connection restored");
            _networkEvents.InitiateChangeState(
                NetworkStatusChangedEvent.New(NetworkStatus.ConnectionRestored));
        }
    }

    public void SetLazyNetworkProvider(Lazy<NetworkProvider> lazyNetworkProvider)
    {
        _lazyNetworkProvider = lazyNetworkProvider;
    }
    
    private NetworkProvider? GetNetworkProvider()
    {
        if (_lazyNetworkProvider != null)
            return _lazyNetworkProvider.Value;
            
        return null;
    }

    private string CreateOperationKey(string operationName, uint connectId, DateTime startTime)
    {
        return $"{operationName}_{connectId}_{startTime.Ticks}";
    }

    private void StartTrackingOperation(string operationName, uint connectId, int maxRetries, string operationKey, Func<Task<Result<object, NetworkFailure>>>? operation = null)
    {
        RetryOperationInfo operationInfo = new()
        {
            OperationName = operationName,
            ConnectId = connectId,
            StartTime = DateTime.UtcNow,
            CurrentRetryCount = 1,
            MaxRetries = maxRetries,
            UniqueKey = operationKey,
            IsExhausted = false,
            Operation = operation
        };

        _activeRetryOperations.TryAdd(operationKey, operationInfo);
        Log.Debug("🟡 STARTED TRACKING: Operation {OperationName} on ConnectId {ConnectId} - Key: {OperationKey}. Active operations: {ActiveCount}", 
            operationName, connectId, operationKey, _activeRetryOperations.Count);
    }

    private void UpdateOperationRetryCount(string operationKey, int retryCount)
    {
        if (_activeRetryOperations.TryGetValue(operationKey, out RetryOperationInfo? operation))
        {
            operation.CurrentRetryCount = retryCount;
            Log.Debug("📊 UPDATED TRACKING: Key {OperationKey} - Retry count: {RetryCount}/{MaxRetries}", 
                operationKey, retryCount, operation.MaxRetries);
        }
    }

    private void StopTrackingOperation(string operationKey, string reason)
    {
        if (_activeRetryOperations.TryRemove(operationKey, out RetryOperationInfo? operation))
        {
            Log.Debug("🟢 STOPPED TRACKING: Operation {OperationName} on ConnectId {ConnectId} - Reason: {Reason}. Remaining active operations: {ActiveCount}",
                operation.OperationName, operation.ConnectId, reason, _activeRetryOperations.Count);
        }
    }

    private void MarkOperationAsExhausted(string operationKey)
    {
        if (_activeRetryOperations.TryGetValue(operationKey, out RetryOperationInfo? operation))
        {
            operation.IsExhausted = true;
            Log.Debug("🔴 MARKED AS EXHAUSTED: Operation {OperationName} on ConnectId {ConnectId} - Key: {OperationKey}",
                operation.OperationName, operation.ConnectId, operationKey);
        }
    }

    private bool AreAllRetryOperationsExhausted()
    {
        if (_activeRetryOperations.IsEmpty)
        {
            Log.Debug("🔍 EXHAUSTION CHECK: No active operations");
            return false;
        }

        bool allExhausted = _activeRetryOperations.Values.All(op => op.IsExhausted);
        int exhaustedCount = _activeRetryOperations.Values.Count(op => op.IsExhausted);
        int totalCount = _activeRetryOperations.Count;
        
        Log.Debug("🔍 EXHAUSTION CHECK: {ExhaustedCount}/{TotalCount} operations exhausted. All exhausted: {AllExhausted}",
            exhaustedCount, totalCount, allExhausted);
        return allExhausted;
    }

    private async Task<bool> RetryAllExhaustedOperationsAsync()
    {
        var exhaustedOperations = _activeRetryOperations.Values
            .Where(op => op.IsExhausted && op.Operation != null)
            .ToList();

        if (!exhaustedOperations.Any())
        {
            Log.Debug("🔄 MANUAL RETRY: No exhausted operations to retry");
            return false;
        }

        Log.Information("🔄 MANUAL RETRY: Retrying {Count} exhausted operations", exhaustedOperations.Count);

        bool anySucceeded = false;
        foreach (var operation in exhaustedOperations)
        {
            try
            {
                operation.IsExhausted = false;
                operation.CurrentRetryCount = 0;
                
                var result = await operation.Operation!();
                if (result.IsOk)
                {
                    StopTrackingOperation(operation.UniqueKey, "Manual retry succeeded");
                    anySucceeded = true;
                }
                else
                {
                    operation.IsExhausted = true;
                    Log.Debug("🔄 MANUAL RETRY: Operation {OperationName} failed again during manual retry", operation.OperationName);
                }
            }
            catch (Exception ex)
            {
                operation.IsExhausted = true;
                Log.Error(ex, "🔄 MANUAL RETRY: Error retrying operation {OperationName}", operation.OperationName);
            }
        }

        Log.Information("🔄 MANUAL RETRY: Completed. Any succeeded: {AnySucceeded}", anySucceeded);
        return anySucceeded;
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 15,
        CancellationToken cancellationToken = default)
    {
        if (!connectId.HasValue)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Connection ID is required"));
        }

        DateTime operationStartTime = DateTime.UtcNow;
        string operationKey = CreateOperationKey(operationName, connectId.Value, operationStartTime);

        Func<Task<Result<object, NetworkFailure>>> wrappedOperation = async () =>
        {
            var result = await operation();
            return result.IsOk 
                ? Result<object, NetworkFailure>.Ok(result.Unwrap()!)
                : Result<object, NetworkFailure>.Err(result.UnwrapErr());
        };

        Context context = new Context
        {
            ["OperationName"] = operationName,
            ["ConnectId"] = connectId.Value,
            ["MaxRetries"] = maxRetries,
            ["OperationKey"] = operationKey,
            ["OperationStartTime"] = operationStartTime,
            ["WrappedOperation"] = wrappedOperation
        };

        try
        {
            object result = await _retryPolicy.ExecuteAsync(
                async (ctx, ct) => await operation().ConfigureAwait(false),
                context,
                cancellationToken).ConfigureAwait(false);

            Result<TResponse, NetworkFailure> typedResult = (Result<TResponse, NetworkFailure>)result;
            
            if (typedResult.IsOk)
            {
                StopTrackingOperation(operationKey, "Completed successfully");
            }

            return typedResult;
        }
        catch (OperationCanceledException)
        {
            StopTrackingOperation(operationKey, "Operation cancelled");
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Operation cancelled"));
        }
        catch (Exception ex)
        {
            StopTrackingOperation(operationKey, $"Unexpected error: {ex.Message}");
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Unexpected error: {ex.Message}"));
        }
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId,
        System.Collections.Generic.IReadOnlyList<TimeSpan> backoffSchedule,
        bool useJitter = true,
        double jitterRatio = 0.25,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSecrecyChannelOperationAsync(
            operation,
            operationName,
            connectId,
            backoffSchedule.Count,
            cancellationToken);
    }

    private async Task<bool> EnsureSecrecyChannelAsync(uint connectId, CancellationToken cancellationToken)
    {
        NetworkProvider? networkProvider = GetNetworkProvider();
        if (networkProvider == null)
        {
            return false;
        }

        try
        {
            if (networkProvider.IsConnectionHealthy(connectId))
            {
                return true;
            }


            Result<bool, NetworkFailure> restoreResult = await networkProvider.TryRestoreConnectionAsync(connectId).ConfigureAwait(false);
            
            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private IAsyncPolicy<object> CreateRetryPolicy()
    {
        System.Collections.Generic.IEnumerable<TimeSpan> rawDelays = _configuration.UseAdaptiveRetry 
            ? Backoff.DecorrelatedJitterBackoffV2(
                medianFirstRetryDelay: _configuration.InitialRetryDelay,
                retryCount: _configuration.MaxRetries,
                fastFirst: true)
            : Backoff.ExponentialBackoff(
                initialDelay: _configuration.InitialRetryDelay,
                retryCount: _configuration.MaxRetries,
                factor: 2.0,
                fastFirst: true);
        
        TimeSpan[] retryDelays = rawDelays.Select(delay => 
            delay > _configuration.MaxRetryDelay ? _configuration.MaxRetryDelay : delay).ToArray();

        IAsyncPolicy<object> retryPolicy = Policy
            .HandleResult<object>(result =>
            {
                if (result is Result<object, NetworkFailure> res)
                    return res.IsErr && ShouldRetry(res.UnwrapErr());
                
                if (TryGetNetworkFailureFromResult(result, out NetworkFailure? failure))
                {
                    return ShouldRetry(failure);
                }
                return false;
            })
            .WaitAndRetryAsync(
                retryDelays,
                onRetry: async (outcome, delay, retryCount, context) =>
                {
                    object operation = context.ContainsKey("OperationName") ? context["OperationName"] : "Unknown";
                    object connectId = context.ContainsKey("ConnectId") ? context["ConnectId"] : 0;
                    string operationKey = context.ContainsKey("OperationKey") ? (string)context["OperationKey"] : string.Empty;
                    int maxRetries = context.ContainsKey("MaxRetries") ? (int)context["MaxRetries"] : retryDelays.Length;

                    // Start tracking on first retry (retryCount == 1)
                    if (retryCount == 1)
                    {
                        Func<Task<Result<object, NetworkFailure>>>? wrappedOp = 
                            context.ContainsKey("WrappedOperation") 
                                ? (Func<Task<Result<object, NetworkFailure>>>)context["WrappedOperation"] 
                                : null;
                        StartTrackingOperation(operation.ToString()!, (uint)connectId, maxRetries, operationKey, wrappedOp);
                    }
                    else
                    {
                        UpdateOperationRetryCount(operationKey, retryCount);
                    }

                    // Check if this is the FINAL retry attempt (all delays exhausted)
                    if (retryCount >= retryDelays.Length)
                    {
                        Log.Warning("🔴 RETRY DELAYS EXHAUSTED: Operation {OperationName} on ConnectId {ConnectId} - Attempt {RetryCount}/{MaxRetries} exhausted all retries", 
                            operation, connectId, retryCount, maxRetries);
                        
                        // Mark this operation as exhausted but keep it in tracking for potential manual retry
                        MarkOperationAsExhausted(operationKey);
                        
                        // First show window without retry button (server not responding)
                        if (_activeRetryOperations.Values.Count(op => op.IsExhausted) == 1)
                        {
                            Log.Information("🔴 FIRST OPERATION EXHAUSTED: Showing notification window without retry button");
                            _networkEvents.InitiateChangeState(
                                NetworkStatusChangedEvent.New(NetworkStatus.DataCenterDisconnected));
                        }
                        
                        // Only emit RetriesExhausted signal if ALL operations are now exhausted
                        if (AreAllRetryOperationsExhausted())
                        {
                            Log.Warning("🔴 ALL OPERATIONS EXHAUSTED: All retry operations are exhausted. Now showing retry button");
                            _networkEvents.InitiateChangeState(
                                NetworkStatusChangedEvent.New(NetworkStatus.RetriesExhausted));
                        }
                        else
                        {
                            Log.Debug("⏳ OTHER OPERATIONS STILL RETRYING: Not showing retry button yet. Exhausted operations: {ExhaustedCount}", 
                                _activeRetryOperations.Values.Count(op => op.IsExhausted));
                        }
                    }
                    else
                    {
                        Log.Debug("Retrying operation {OperationName} on ConnectId {ConnectId} - Attempt {RetryCount}/{MaxRetries}, next delay: {Delay}ms", 
                            operation, connectId, retryCount, maxRetries, delay.TotalMilliseconds);
                    }

                    if (RequiresConnectionRecovery(outcome.Result))
                    {
                        await EnsureSecrecyChannelAsync((uint)connectId, CancellationToken.None).ConfigureAwait(false);
                    }
                });

        IAsyncPolicy<object> circuitBreakerPolicy = Policy
            .HandleResult<object>(result =>
            {
                if (result is Result<object, NetworkFailure> res)
                    return res.IsErr && IsCircuitBreakerFailure(res.UnwrapErr());
                return false;
            })
            .CircuitBreakerAsync(
                _configuration.CircuitBreakerThreshold,
                _configuration.CircuitBreakerDuration,
                onBreak: (outcome, duration) =>
                {
                },
                onReset: () =>
                {
                });

        IAsyncPolicy timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromSeconds(30), // Reduced from 1 minute to prevent deadlocks
            onTimeoutAsync: (context, timespan, task) =>
            {
                if (context.ContainsKey("OperationName"))
                {
                    Log.Warning("Operation {OperationName} timed out after {Timeout}", 
                        context["OperationName"], timespan);
                }
                return Task.CompletedTask;
            });

        return retryPolicy
            .WrapAsync(circuitBreakerPolicy)
            .WrapAsync(timeoutPolicy);
    }

    private bool ShouldRetry(NetworkFailure failure)
    {
        return FailureClassification.IsTransient(failure);
    }

    private bool RequiresConnectionRecovery(object? result)
    {
        if (result is Result<object, NetworkFailure> res && res.IsErr)
        {
            NetworkFailure failure = res.UnwrapErr();
            return FailureClassification.IsProtocolStateMismatch(failure) ||
                   FailureClassification.IsChainRotationMismatch(failure) ||
                   FailureClassification.IsCryptoDesync(failure) ||
                   failure.Message.Contains("Connection unavailable", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private bool IsCircuitBreakerFailure(NetworkFailure failure)
    {
        return FailureClassification.IsServerShutdown(failure);
    }


    public void ResetConnectionState(uint? connectId = null)
    {
        if (connectId.HasValue)
        {
            GetNetworkProvider()?.ClearConnection(connectId.Value);
        }
    }

    public RetryMetrics GetRetryMetrics(uint? connectId = null)
    {
        return new RetryMetrics(0, 0, 0, TimeSpan.Zero, DateTime.MinValue, DateTime.MinValue);
    }

    public ConnectionRetryState? GetConnectionState(uint connectId)
    {
        bool isHealthy = GetNetworkProvider()?.IsConnectionHealthy(connectId) ?? false;
        return new ConnectionRetryState(
            connectId,
            0,
            DateTime.MinValue,
            null,
            !isHealthy,
            !isHealthy ? DateTime.UtcNow : null);
    }

    public void MarkConnectionHealthy(uint connectId)
    {
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        return GetNetworkProvider()?.IsConnectionHealthy(connectId) ?? false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Configuration binding is safe for simple types")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling", Justification = "Configuration binding is safe for simple types")]
    private static ImprovedRetryConfiguration GetRetryConfiguration(IConfiguration configuration)
    {
        return configuration.GetSection("ImprovedRetryPolicy").Get<ImprovedRetryConfiguration>() 
               ?? ImprovedRetryConfiguration.Production;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMemberTypes' requirements", Justification = "Reflection is used safely on known Result types")]
    private static bool TryGetNetworkFailureFromResult(object result, [NotNullWhen(true)] out NetworkFailure? failure)
    {
        failure = null;
        
        try
        {
            var type = result.GetType();
            var isErrProperty = type.GetProperty("IsErr");
            if (isErrProperty != null && (bool)isErrProperty.GetValue(result)!)
            {
                var unwrapErrMethod = type.GetMethod("UnwrapErr");
                if (unwrapErrMethod != null)
                {
                    failure = unwrapErrMethod.Invoke(result, null) as NetworkFailure;
                    return failure != null;
                }
            }
        }
        catch
        {
            // Reflection failed, return false
        }
        
        return false;
    }

    public void Dispose()
    {
    }
}