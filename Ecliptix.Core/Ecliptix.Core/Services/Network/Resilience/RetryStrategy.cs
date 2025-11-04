using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Timeout;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Core.Services.Network.Resilience;

public sealed class RetryStrategy : IRetryStrategy
{
    private const int MAX_TRACKED_OPERATIONS = 1000;

    private const int CLEANUP_INTERVAL_MINUTES = 5;

    private const int OPERATION_TIMEOUT_MINUTES = 10;

    private const string ATTEMPT_KEY = "attempt";

    private readonly RetryStrategyConfiguration _strategyConfiguration;
    private readonly IConnectivityService _connectivityService;
    private readonly ConcurrentDictionary<string, RetryOperationInfo> _activeRetryOperations = new();
    private readonly ConcurrentDictionary<string, object> _pipelineCache = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private readonly IDisposable? _manualRetrySubscription;

    private Lazy<NetworkProvider>? _lazyNetworkProvider;
    private volatile bool _isDisposed;

    private sealed class RetryOperationInfo
    {
        public required string OperationName { get; init; }
        public required uint ConnectId { get; init; }
        public required DateTime StartTime { get; init; }
        public required int MAX_RETRIES { get; init; }
        public required string UniqueKey { get; init; }
        public required RpcServiceType? ServiceType { get; init; }

        private int _currentRetryCount;
        private int _isExhausted;

        public int CurrentRetryCount
        {
            get => _currentRetryCount;
            set => Interlocked.Exchange(ref _currentRetryCount, value);
        }

        public bool IsExhausted
        {
            get => _isExhausted == 1;
            set => Interlocked.Exchange(ref _isExhausted, value ? 1 : 0);
        }
    }

    public RetryStrategy(
        RetryStrategyConfiguration strategyConfiguration,
        IConnectivityService connectivityService,
        IOperationTimeoutProvider _)
    {
        _strategyConfiguration = strategyConfiguration;
        _connectivityService = connectivityService;

        _cleanupTimer = new Timer(
            CleanupAbandonedOperations,
            null,
            TimeSpan.FromMinutes(CLEANUP_INTERVAL_MINUTES),
            TimeSpan.FromMinutes(CLEANUP_INTERVAL_MINUTES));

        try
        {
            _manualRetrySubscription = _connectivityService.OnManualRetryRequested(HandleManualRetryRequestAsync);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to subscribe to manual retry events");
            Dispose();
            throw;
        }

        Log.Information(
            "SecrecyChannelRetryStrategy initialized with configuration: MAX_RETRIES={MAX_RETRIES}, InitialDelay={InitialDelay}",
            _strategyConfiguration.MAX_RETRIES, _strategyConfiguration.INITIAL_RETRY_DELAY);
    }

    public void SetLazyNetworkProvider(Lazy<NetworkProvider> lazyNetworkProvider)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RetryStrategy));
        }

        _lazyNetworkProvider = lazyNetworkProvider;
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteRpcOperationAsync<TResponse>(
        Func<int, CancellationToken, Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint connectId,
        RpcServiceType? serviceType = null,
        int? maxRetries = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSecrecyChannelOperationInternalAsync(
                operation, operationName, connectId, serviceType, maxRetries, cancellationToken,
                bypassExhaustionCheck: false)
            .ConfigureAwait(false);
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteManualRetryRpcOperationAsync<TResponse>(
        Func<int, CancellationToken, Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint connectId,
        RpcServiceType? serviceType = null,
        int? maxRetries = null,
        CancellationToken cancellationToken = default)
    {
        Log.Information("🔄 MANUAL RETRY: Executing operation '{OperationName}' bypassing exhaustion checks",
            operationName);
        return await ExecuteSecrecyChannelOperationInternalAsync(
                operation, operationName, connectId, serviceType, maxRetries, cancellationToken,
                bypassExhaustionCheck: true)
            .ConfigureAwait(false);
    }

    private async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationInternalAsync<TResponse>(
        Func<int, CancellationToken, Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId,
        RpcServiceType? serviceType,
        int? maxRetries,
        CancellationToken cancellationToken,
        bool bypassExhaustionCheck)
    {
        uint actualConnectId = connectId ?? 0;
        int actualMaxRetries = maxRetries ?? _strategyConfiguration.MAX_RETRIES;

        if (!bypassExhaustionCheck && await IsGloballyExhaustedAsync().ConfigureAwait(false))
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("All operations exhausted, manual retry required"));
        }

        DateTime operationStartTime = DateTime.UtcNow;
        string operationKey = CreateOperationKey(operationName, actualConnectId, operationStartTime);

        try
        {
            IAsyncPolicy<Result<TResponse, NetworkFailure>> retryPolicy =
                CreateTypedRetryPolicy<TResponse>(
                    actualMaxRetries,
                    operationKey,
                    operationName,
                    actualConnectId,
                    serviceType,
                    cancellationToken);

            Context context = new(operationName) { [ATTEMPT_KEY] = 1 };

            Result<TResponse, NetworkFailure> result = await retryPolicy.ExecuteAsync(
                (ctx, ct) => ExecuteOperationWithLogging(ctx, ct, operation, operationName),
                context,
                cancellationToken).ConfigureAwait(false);

            if (result.IsOk)
            {
                StopTrackingOperation(operationKey, "Completed successfully");
            }
            else
            {
                Log.Warning(
                    "🔴 OPERATION FAILED: Operation {OperationName} failed after all retries with error: {ERROR}",
                    operationName, result.UnwrapErr().Message);
                Log.Debug("🔴 KEEPING EXHAUSTED: Keeping operation {OperationName} tracked for retry button detection",
                    operationName);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StopTrackingOperation(operationKey, "Operation cancelled");
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.OperationCancelled("Operation cancelled by user"));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "🚨 UNEXPECTED ERROR: Operation {OperationName} failed with unexpected exception",
                operationName);
            StopTrackingOperation(operationKey, $"Unexpected error: {ex.Message}");
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Unexpected error: {ex.Message}"));
        }
    }

    public void MarkConnectionHealthy(uint connectId)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RetryStrategy));
        }

        Task.Run(async () =>
        {
            if (_isDisposed)
            {
                return;
            }

            try
            {
                await _stateLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    int resetCount = 0;
                    foreach (RetryOperationInfo operation in _activeRetryOperations.Values.ToArray())
                    {
                        if (operation.ConnectId == connectId && operation.IsExhausted)
                        {
                            operation.IsExhausted = false;
                            operation.CurrentRetryCount = 0;
                            Log.Information(
                                "🔄 CONNECTION HEALTHY: Marking operation {OperationName} as healthy for ConnectId {ConnectId}",
                                operation.OperationName, connectId);

                            StopTrackingOperation(operation.UniqueKey, "Connection restored");
                            resetCount++;
                        }
                    }

                    if (resetCount > 0)
                    {
                        Log.Information(
                            "🔄 CONNECTION HEALTHY: Reset exhaustion state for ConnectId {ConnectId}, cleaned up {Count} operations",
                            connectId, resetCount);
                    }
                }
                finally
                {
                    _stateLock.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to mark connection healthy for ConnectId {ConnectId}", connectId);
            }
        }).ContinueWith(task =>
        {
            if (task.IsFaulted && task.Exception != null)
            {
                Log.Error(task.Exception.GetBaseException(),
                    "Unhandled exception in MarkConnectionHealthy background task for ConnectId {ConnectId}",
                    connectId);
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void ClearExhaustedOperations()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(RetryStrategy));
        }

        try
        {
            List<string> exhaustedKeys = [];
            exhaustedKeys.AddRange(from operation in _activeRetryOperations.Values
                where operation.IsExhausted
                select operation.UniqueKey);

            foreach (string key in exhaustedKeys)
            {
                if (_activeRetryOperations.TryRemove(key, out RetryOperationInfo? operation) && Log.IsEnabled(LogEventLevel.Debug))
                {
                    Log.Debug("🔄 CLEARED: Removed exhausted operation {OperationName} for fresh retry",
                        operation.OperationName);
                }
            }

            if (exhaustedKeys.Count > 0)
            {
                Log.Information("🔄 CLEARED: Removed {Count} exhausted operations to allow fresh retry",
                    exhaustedKeys.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to clear exhausted operations");
        }
    }

    private async Task<bool> IsGloballyExhaustedAsync(RpcServiceType? serviceType = null)
    {
        if (_activeRetryOperations.IsEmpty)
        {
            return false;
        }

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            bool hasAnyOperationsForService = false;
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values.ToArray())
            {
                if (serviceType.HasValue && operation.ServiceType != serviceType.Value)
                {
                    continue;
                }

                hasAnyOperationsForService = true;
                if (!operation.IsExhausted)
                {
                    return false;
                }
            }

            return hasAnyOperationsForService;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task HandleManualRetryRequestAsync(ManualRetryRequestedEvent evt)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            ClearExhaustedOperations();

            NetworkProvider networkProvider = GetNetworkProvider();
            Result<Unit, NetworkFailure> retryResult =
                await networkProvider.ForceFreshConnectionAsync().ConfigureAwait(false);

            if (retryResult.IsOk)
            {
                Log.Information("🔄 MANUAL RETRY: Connection restored successfully");
                NotifyConnectionRestored(evt.ConnectId);
            }
            else
            {
                NetworkFailure failure = retryResult.UnwrapErr();
                Log.Warning("🔄 MANUAL RETRY: Connection restore failed: {ERROR}", failure.Message);
                NotifyConnectionFailed(failure, evt.ConnectId);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "🚨 MANUAL RETRY ERROR: Failed to handle manual retry request");
        }
    }

    private void NotifyConnectionRestored(uint? connectId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _ = _connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(
                        connectId,
                        ConnectivityReason.ManualRetry)).ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            Log.Error(task.Exception, "Unhandled exception publishing connection restored");
                        }
                    },
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to notify UI of connection restored state");
            }
        });
    }

    private void NotifyConnectionFailed(NetworkFailure failure, uint? connectId)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _ = _connectivityService.PublishAsync(
                    ConnectivityIntent.Disconnected(failure, connectId)).ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            Log.Error(task.Exception, "Unhandled exception publishing disconnected state");
                        }
                    },
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to notify UI of disconnected state");
            }
        });
    }

    private void StartTrackingOperation(string operationName, uint connectId, int maxRetries, string operationKey,
        RpcServiceType? serviceType)
    {
        if (_activeRetryOperations.Count >= MAX_TRACKED_OPERATIONS)
        {
            Log.Warning("Maximum tracked operations limit reached ({MaxOperations}), cleaning up old operations",
                MAX_TRACKED_OPERATIONS);
            CleanupAbandonedOperations(null);
        }

        RetryOperationInfo operationInfo = new()
        {
            OperationName = operationName,
            ConnectId = connectId,
            StartTime = DateTime.UtcNow,
            MAX_RETRIES = maxRetries,
            UniqueKey = operationKey,
            ServiceType = serviceType,
            CurrentRetryCount = 1,
            IsExhausted = false
        };

        _activeRetryOperations.TryAdd(operationKey, operationInfo);
        Log.Debug(
            "🟡 STARTED TRACKING: Operation {OperationName} (ServiceType: {ServiceType}) on ConnectId {ConnectId} - Key: {OperationKey}. Active operations: {ActiveCount}",
            operationName, serviceType?.ToString() ?? "Unknown", connectId, operationKey, _activeRetryOperations.Count);
    }

    private void UpdateOperationRetryCount(string operationKey, int retryCount)
    {
        if (_activeRetryOperations.TryGetValue(operationKey, out RetryOperationInfo? operation))
        {
            operation.CurrentRetryCount = retryCount;
            Log.Debug("📊 UPDATED TRACKING: Key {OperationKey} - Retry count: {RetryCount}/{MAX_RETRIES}",
                operationKey, retryCount, operation.MAX_RETRIES);
        }
    }

    private void StopTrackingOperation(string operationKey, string reason)
    {
        if (_activeRetryOperations.TryRemove(operationKey, out RetryOperationInfo? operation))
        {
            Log.Debug(
                "🟢 STOPPED TRACKING: Operation {OperationName} on ConnectId {ConnectId} - Reason: {Reason}. Remaining active operations: {ActiveCount}",
                operation.OperationName, operation.ConnectId, reason, _activeRetryOperations.Count);
        }
    }

    private void MarkOperationAsExhausted(string operationKey)
    {
        if (_activeRetryOperations.TryGetValue(operationKey, out RetryOperationInfo? operation))
        {
            operation.IsExhausted = true;
            Log.Debug(
                "🔴 MARKED AS EXHAUSTED: Operation {OperationName} on ConnectId {ConnectId} - Key: {OperationKey}",
                operation.OperationName, operation.ConnectId, operationKey);
        }
    }

    private void CleanupAbandonedOperations(object? state)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-OPERATION_TIMEOUT_MINUTES);
            List<string> abandonedKeys = new();
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values.ToArray())
            {
                if (operation.StartTime < cutoff)
                {
                    abandonedKeys.Add(operation.UniqueKey);
                }
            }

            foreach (string key in abandonedKeys)
            {
                if (_activeRetryOperations.TryRemove(key, out RetryOperationInfo? operation) && Serilog.Log.IsEnabled(LogEventLevel.Debug))
                {
                    Log.Debug("🧹 CLEANUP: Removed abandoned operation {OperationName} after {Minutes} minutes",
                        operation.OperationName, OPERATION_TIMEOUT_MINUTES);
                }
            }

            if (abandonedKeys.Count > 0)
            {
                Log.Information("🧹 CLEANUP: Removed {Count} abandoned operations from tracking", abandonedKeys.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ERROR during operation cleanup");
        }
    }

    private string CreateDelaysCacheKey(int maxRetries)
    {
        return $"delays_{maxRetries}";
    }

    private TimeSpan[] GetOrCreateRetryDelays(int maxRetries)
    {
        string cacheKey = CreateDelaysCacheKey(maxRetries);

        if (_pipelineCache.TryGetValue(cacheKey, out object? cachedDelays) && cachedDelays is TimeSpan[] delays)
        {
            if (Log.IsEnabled(LogEventLevel.Debug))
            {
                Log.Debug("📦 RETRY DELAYS CACHE HIT: Reusing cached delays for maxRetries={MAX_RETRIES}",
                    maxRetries);
            }

            return delays;
        }

        if (Log.IsEnabled(LogEventLevel.Debug))
        {
            Log.Debug("🏗️ RETRY DELAYS CACHE MISS: Calculating new delays for maxRetries={MAX_RETRIES}",
                maxRetries);
        }

        TimeSpan baseDelay = _strategyConfiguration.INITIAL_RETRY_DELAY;
        TimeSpan maxDelay = _strategyConfiguration.MAX_RETRY_DELAY;
        bool useJitter = _strategyConfiguration.USE_ADAPTIVE_RETRY;

        IEnumerable<TimeSpan> rawDelays = useJitter
            ? Backoff.DecorrelatedJitterBackoffV2(
                medianFirstRetryDelay: baseDelay,
                retryCount: maxRetries,
                fastFirst: true)
            : Backoff.ExponentialBackoff(
                initialDelay: baseDelay,
                retryCount: maxRetries,
                factor: 2.0,
                fastFirst: true);

        TimeSpan[] retryDelays = rawDelays.Select(delay =>
            delay > maxDelay ? maxDelay : delay).ToArray();

        _pipelineCache.TryAdd(cacheKey, retryDelays);

        return retryDelays;
    }

    private IAsyncPolicy<Result<TResponse, NetworkFailure>> CreateTypedRetryPolicy<TResponse>(
        int maxRetries,
        string operationKey,
        string operationName,
        uint connectId,
        RpcServiceType? serviceType,
        CancellationToken cancellationToken)
    {
        TimeSpan[] retryDelays = GetOrCreateRetryDelays(maxRetries);

        ShouldRetryDelegate<TResponse>
            shouldRetryDelegate = RetryDecisionFactory.CreateShouldRetryDelegate<TResponse>();
        RequiresConnectionRecoveryDelegate<TResponse> connectionRecoveryDelegate =
            RetryDecisionFactory.CreateConnectionRecoveryDelegate<TResponse>();

        IAsyncPolicy<Result<TResponse, NetworkFailure>> retryPolicy = Policy<Result<TResponse, NetworkFailure>>
            .Handle<TimeoutRejectedException>()
            .OrResult(result => shouldRetryDelegate(result))
            .WaitAndRetryAsync(
                retryDelays,
                async (outcome, delay, retryCount, context) =>
                {
                    context[ATTEMPT_KEY] = retryCount + 1;

                    bool isTimeout = outcome.Exception is TimeoutRejectedException;
                    bool hasResult = outcome.Exception is null;
                    Result<TResponse, NetworkFailure> result = hasResult ? outcome.Result : default;

                    if (isTimeout)
                    {
                        Log.Warning(
                            "⏳ TIMEOUT: Operation {OperationName} on ConnectId {ConnectId} timed out - attempt {RetryCount}/{MAX_RETRIES}, retrying after {DelayMs}ms",
                            operationName, connectId, retryCount, retryDelays.Length, delay.TotalMilliseconds);
                    }
                    else
                    {
                        Log.Information(
                            "🔄 RETRY ATTEMPT: Operation {OperationName} on ConnectId {ConnectId} - Attempt {RetryCount}/{MAX_RETRIES}, delay: {DelayMs}ms",
                            operationName, connectId, retryCount, retryDelays.Length, delay.TotalMilliseconds);
                    }

                    if (retryCount == 1)
                    {
                        StartTrackingOperation(operationName, connectId, retryDelays.Length, operationKey, serviceType);
                    }
                    else
                    {
                        UpdateOperationRetryCount(operationKey, retryCount);
                    }

                    NetworkFailure? currentFailure = null;
                    if (hasResult && result.IsErr)
                    {
                        currentFailure = result.UnwrapErr();
                    }

                    if (isTimeout && currentFailure == null)
                    {
                        currentFailure = NetworkFailure.DataCenterNotResponding("Operation timeout");
                    }

                    NetworkFailure recoveringFailure = currentFailure ??
                                                       NetworkFailure.DataCenterNotResponding("Retrying operation");

                    await _connectivityService.PublishAsync(
                            ConnectivityIntent.Recovering(recoveringFailure, connectId, retryCount + 1, delay))
                        .ConfigureAwait(false);

                    if (retryCount == retryDelays.Length)
                    {
                        Log.Warning(
                            "🔴 RETRY DELAYS EXHAUSTED: Operation {OperationName} on ConnectId {ConnectId} - Attempt {RetryCount}/{MAX_RETRIES} exhausted all retries",
                            operationName, connectId, retryCount, retryDelays.Length);

                        MarkOperationAsExhausted(operationKey);

                        NetworkProvider? networkProvider = GetNetworkProvider();
                        networkProvider?.BeginSecrecyChannelEstablishRecovery();

                        bool allExhausted = await IsGloballyExhaustedAsync().ConfigureAwait(false);
                        if (allExhausted)
                        {
                            Log.Warning(
                                "🔴 ALL OPERATIONS EXHAUSTED: All retry operations are exhausted. Recovery will handle retry button display");

                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    _connectivityService.PublishAsync(
                                        ConnectivityIntent.RetriesExhausted(
                                            currentFailure ??
                                            NetworkFailure.DataCenterNotResponding("Retries exhausted"),
                                            connectId,
                                            retryCount)).ContinueWith(
                                        task =>
                                        {
                                            if (task.IsFaulted && task.Exception != null)
                                            {
                                                Log.Error(task.Exception, "Unhandled exception publishing retries exhausted");
                                            }
                                        },
                                        TaskScheduler.Default);
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "Failed to notify UI of exhausted state");
                                }
                            });
                        }
                        else
                        {
                            int exhaustedCount = 0;
                            foreach (RetryOperationInfo operation in _activeRetryOperations.Values.ToArray())
                            {
                                if (operation.IsExhausted)
                                {
                                    exhaustedCount++;
                                }
                            }

                            if (exhaustedCount == 1)
                            {
                                Log.Information(
                                    "🔴 FIRST OPERATION EXHAUSTED: Showing notification window without retry button");

                                Dispatcher.UIThread.Post(() =>
                                {
                                    try
                                    {
                                        _connectivityService.PublishAsync(
                                            ConnectivityIntent.Disconnected(
                                                currentFailure ??
                                                NetworkFailure.DataCenterNotResponding("Connection lost"),
                                                connectId)).ContinueWith(
                                            task =>
                                            {
                                                if (task.IsFaulted && task.Exception != null)
                                                {
                                                    Log.Error(task.Exception, "Unhandled exception publishing disconnected state (first exhausted)");
                                                }
                                            },
                                            TaskScheduler.Default);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex, "Failed to notify UI of disconnected state");
                                    }
                                });
                            }

                            if (Log.IsEnabled(LogEventLevel.Debug))
                            {
                                Log.Debug(
                                    "⏳ OTHER OPERATIONS STILL RETRYING: Not showing retry button yet. Exhausted operations: {ExhaustedCount}",
                                    exhaustedCount);
                            }
                        }
                    }

                    bool requiresRecovery = false;
                    if (isTimeout)
                    {
                        requiresRecovery = true;
                    }
                    else if (hasResult)
                    {
                        requiresRecovery = connectionRecoveryDelegate(result);
                    }

                    if (requiresRecovery)
                    {
                        if (_isDisposed || cancellationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        await EnsureSecrecyChannelAsync(connectId, cancellationToken).ConfigureAwait(false);
                    }
                });

        IAsyncPolicy<Result<TResponse, NetworkFailure>> timeoutPolicy = Policy
            .TimeoutAsync<Result<TResponse, NetworkFailure>>(
                (context) =>
                {
                    int currentAttempt = GetCurrentAttempt(context);
                    TimeSpan timeout = _strategyConfiguration.PER_ATTEMPT_TIMEOUT;

                    if (Log.IsEnabled(LogEventLevel.Debug))
                    {
                        Log.Debug(
                            "⏱️ TIMEOUT CALCULATED: Operation {OperationName} attempt {Attempt} timeout set to {TimeoutSeconds}s",
                            operationName, currentAttempt, timeout.TotalSeconds);
                    }

                    return timeout;
                },
                TimeoutStrategy.Pessimistic,
                onTimeoutAsync: (context, timespan, _, _) =>
                {
                    int currentAttempt = GetCurrentAttempt(context);
                    Log.Warning(
                        "⏰ TIMEOUT: Operation {OperationName} on ConnectId {ConnectId} attempt {Attempt} timed out after {TimeoutSeconds}s",
                        operationName, connectId, currentAttempt, timespan.TotalSeconds);
                    return Task.CompletedTask;
                });

        IAsyncPolicy<Result<TResponse, NetworkFailure>> combinedPolicy =
            Policy.WrapAsync(retryPolicy, timeoutPolicy);

        return combinedPolicy;
    }

    private async Task EnsureSecrecyChannelAsync(uint connectId, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        NetworkProvider networkProvider = GetNetworkProvider();

        try
        {
            networkProvider.BeginSecrecyChannelEstablishRecovery();

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (networkProvider.IsConnectionHealthy(connectId))
            {
                return;
            }

            Log.Debug("Attempting to restore connection for ConnectId {ConnectId}", connectId);
            Result<bool, NetworkFailure> restoreResult =
                await networkProvider.TryRestoreConnectionAsync(connectId).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                Log.Information("Connection successfully restored for ConnectId {ConnectId}", connectId);
            }
            else if (restoreResult.IsErr)
            {
                Log.Warning("Failed to restore connection for ConnectId {ConnectId}: {ERROR}", connectId,
                    restoreResult.UnwrapErr().Message);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ERROR attempting to restore connection for ConnectId {ConnectId}", connectId);
        }
    }

    private NetworkProvider GetNetworkProvider()
    {
        if (_lazyNetworkProvider == null || !_lazyNetworkProvider.IsValueCreated)
        {
            throw new InvalidOperationException("NetworkProvider has not been initialized");
        }
        return _lazyNetworkProvider.Value;
    }

    private static string CreateOperationKey(string operationName, uint connectId, DateTime startTime)
    {
        return $"{operationName}_{connectId}_{startTime.Ticks}_{Guid.NewGuid():N}";
    }

    private static int GetCurrentAttempt(Context ctx) =>
        ctx.TryGetValue(ATTEMPT_KEY, out object? val) && val is int attempt ? attempt : 1;

    private async Task<Result<TResponse, NetworkFailure>> ExecuteOperationWithLogging<TResponse>(
        Context ctx,
        CancellationToken ct,
        Func<int, CancellationToken, Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName)
    {
        ct.ThrowIfCancellationRequested();
        int currentAttempt = GetCurrentAttempt(ctx);
        Result<TResponse, NetworkFailure> opResult =
            await operation(currentAttempt, ct).ConfigureAwait(false);
        if (Log.IsEnabled(LogEventLevel.Debug))
        {
            Log.Debug(
                "🔧 RETRY POLICY: Operation '{OperationName}' attempt {Attempt} returned IsOk: {IsOk}",
                operationName, currentAttempt, opResult.IsOk);
        }

        return opResult;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            _manualRetrySubscription?.Dispose();
            _cleanupTimer.Dispose();
            _stateLock.Dispose();

            Log.Information("SecrecyChannelRetryStrategy disposed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ERROR during SecrecyChannelRetryStrategy disposal");
        }
    }
}
