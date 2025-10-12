using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Polly.Wrap;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Services.Network.Resilience;

public sealed class SecrecyChannelRetryStrategy : IRetryStrategy
{
    private readonly ImprovedRetryConfiguration _configuration;
    private readonly INetworkEventService _networkEvents;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ConcurrentDictionary<string, RetryOperationInfo> _activeRetryOperations = new();
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly Timer _cleanupTimer;
    private readonly IDisposable? _manualRetrySubscription;

    private Lazy<NetworkProvider>? _lazyNetworkProvider;
    private volatile bool _isDisposed;
    private const int MaxTrackedOperations = 1000;
    private const int CleanupIntervalMinutes = 5;
    private const int OperationTimeoutMinutes = 10;

    private sealed class RetryOperationInfo
    {
        public required string OperationName { get; init; }
        public required uint ConnectId { get; init; }
        public required DateTime StartTime { get; init; }
        public required int MaxRetries { get; init; }
        public required string UniqueKey { get; init; }

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

    public SecrecyChannelRetryStrategy(
        ImprovedRetryConfiguration configuration,
        INetworkEventService networkEvents,
        IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(networkEvents);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        configuration.ValidateAndThrow();

        _configuration = configuration;
        _networkEvents = networkEvents;
        _uiDispatcher = uiDispatcher;

        _cleanupTimer = new Timer(
            CleanupAbandonedOperations,
            null,
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));

        try
        {
            _manualRetrySubscription = _networkEvents.OnManualRetryRequested(
                evt => HandleManualRetryRequestAsync(evt));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to subscribe to manual retry events");
            Dispose();
            throw;
        }

        Log.Information(
            "SecrecyChannelRetryStrategy initialized with configuration: MaxRetries={MaxRetries}, InitialDelay={InitialDelay}",
            _configuration.MaxRetries, _configuration.InitialRetryDelay);
    }

    public void SetLazyNetworkProvider(Lazy<NetworkProvider> lazyNetworkProvider)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecrecyChannelRetryStrategy));
        _lazyNetworkProvider = lazyNetworkProvider;
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int? maxRetries = null,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSecrecyChannelOperationInternalAsync(
            operation, operationName, connectId, maxRetries, cancellationToken, bypassExhaustionCheck: false);
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteManualRetryOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int? maxRetries = null,
        CancellationToken cancellationToken = default)
    {
        Log.Information("🔄 MANUAL RETRY: Executing operation '{OperationName}' bypassing exhaustion checks",
            operationName);
        return await ExecuteSecrecyChannelOperationInternalAsync(
            operation, operationName, connectId, maxRetries, cancellationToken, bypassExhaustionCheck: true);
    }

    private async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationInternalAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId,
        int? maxRetries,
        CancellationToken cancellationToken,
        bool bypassExhaustionCheck)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecrecyChannelRetryStrategy));
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        if (!connectId.HasValue)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Connection ID is required"));
        }

        int actualMaxRetries = maxRetries ?? _configuration.MaxRetries;

        if (!bypassExhaustionCheck && await IsGloballyExhaustedAsync())
        {
            Log.Information(
                "🚫 OPERATION BLOCKED: Cannot start new operation '{OperationName}' - system is globally exhausted, manual retry required",
                operationName);
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("All operations exhausted, manual retry required"));
        }

        Log.Information("🚀 EXECUTE OPERATION: Starting operation '{OperationName}' on ConnectId {ConnectId}",
            operationName, connectId.Value);

        DateTime operationStartTime = DateTime.UtcNow;
        string operationKey = CreateOperationKey(operationName, connectId.Value, operationStartTime);

        try
        {
            IAsyncPolicy<Result<TResponse, NetworkFailure>> retryPolicy =
                CreateTypedRetryPolicy<TResponse>(actualMaxRetries, operationKey, operationName, connectId.Value);

            Context context = new(operationName);

            Result<TResponse, NetworkFailure> result = await retryPolicy.ExecuteAsync(
                async (_, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    Result<TResponse, NetworkFailure> opResult = await operation().ConfigureAwait(false);
                    Log.Debug("🔧 RETRY POLICY: Operation '{OperationName}' returned IsOk: {IsOk}",
                        operationName, opResult.IsOk);
                    return opResult;
                },
                context,
                cancellationToken).ConfigureAwait(false);

            if (result.IsOk)
            {
                StopTrackingOperation(operationKey, "Completed successfully");
            }
            else
            {
                Log.Warning(
                    "🔴 OPERATION FAILED: Operation {OperationName} failed after all retries with error: {Error}",
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
                NetworkFailure.DataCenterNotResponding("Operation cancelled"));
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
    public void ResetConnectionState(uint? connectId = null)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecrecyChannelRetryStrategy));

        try
        {
            if (!connectId.HasValue) return;
            NetworkProvider? networkProvider = GetNetworkProvider();
            networkProvider?.ClearConnection(connectId.Value);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to reset connection state for ConnectId {ConnectId}", connectId);
        }
    }

    public void MarkConnectionHealthy(uint connectId)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecrecyChannelRetryStrategy));

        _ = Task.Run(async () =>
        {
            try
            {
                await _stateLock.WaitAsync();
                try
                {
                    int resetCount = 0;
                    foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
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
        });
    }

    public bool HasExhaustedOperations()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecrecyChannelRetryStrategy));

        try
        {
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
            {
                if (operation.IsExhausted)
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for exhausted operations");
            return false;
        }
    }

    public void ClearExhaustedOperations()
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecrecyChannelRetryStrategy));

        try
        {
            List<string> exhaustedKeys = new();
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
            {
                if (operation.IsExhausted)
                {
                    exhaustedKeys.Add(operation.UniqueKey);
                }
            }

            foreach (string key in exhaustedKeys)
            {
                if (_activeRetryOperations.TryRemove(key, out RetryOperationInfo? operation))
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

    public void Dispose()
    {
        if (_isDisposed)
            return;

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
            Log.Error(ex, "Error during SecrecyChannelRetryStrategy disposal");
        }
    }

    private async Task<bool> IsGloballyExhaustedAsync()
    {
        if (_activeRetryOperations.IsEmpty)
            return false;

        await _stateLock.WaitAsync();
        try
        {
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
            {
                if (!operation.IsExhausted)
                {
                    return false;
                }
            }
            return true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private bool IsGloballyExhausted()
    {
        if (_activeRetryOperations.IsEmpty)
            return false;

        foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
        {
            if (!operation.IsExhausted)
            {
                return false;
            }
        }
        return true;
    }

    private async Task HandleManualRetryRequestAsync(ManualRetryRequestedEvent evt)
    {
        if (_isDisposed)
            return;

        Log.Information("🔄 MANUAL RETRY REQUEST: Received manual retry request for ConnectId {ConnectId}",
            evt.ConnectId);

        try
        {
            ClearExhaustedOperations();

            bool anySucceeded = await RetryAllExhaustedOperationsAsync();

            if (anySucceeded)
            {
                Log.Information("🔄 MANUAL RETRY: Operations succeeded. Connection restored");
                _uiDispatcher.Post(() =>
                {
                    try
                    {
                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ConnectionRestored);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to notify UI of connection restored state");
                    }
                });
            }
            else
            {
                Log.Information("🔄 MANUAL RETRY: Operations failed. Will retry via normal retry strategy");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "🚨 MANUAL RETRY ERROR: Failed to handle manual retry request");
        }
    }

    private async Task<bool> RetryAllExhaustedOperationsAsync()
    {
        await _stateLock.WaitAsync();
        List<RetryOperationInfo> exhaustedOperations = new();
        try
        {
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
            {
                if (operation.IsExhausted)
                {
                    exhaustedOperations.Add(operation);
                }
            }
        }
        finally
        {
            _stateLock.Release();
        }

        if (exhaustedOperations.Count == 0)
        {
            Log.Debug("🔄 MANUAL RETRY: No exhausted operations to retry");
            return false;
        }

        Log.Information("🔄 MANUAL RETRY: Found {Count} exhausted operations, clearing for fresh retry",
            exhaustedOperations.Count);

        foreach (RetryOperationInfo operation in exhaustedOperations)
        {
            operation.IsExhausted = false;
            operation.CurrentRetryCount = 0;
            StopTrackingOperation(operation.UniqueKey, "Manual retry cleared");
        }

        Log.Information("🔄 MANUAL RETRY: Cleared {Count} exhausted operations for fresh retry",
            exhaustedOperations.Count);
        return true;
    }

    private void StartTrackingOperation(string operationName, uint connectId, int maxRetries, string operationKey)
    {
        if (_activeRetryOperations.Count >= MaxTrackedOperations)
        {
            Log.Warning("Maximum tracked operations limit reached ({MaxOperations}), cleaning up old operations",
                MaxTrackedOperations);
            CleanupAbandonedOperations(null);
        }

        RetryOperationInfo operationInfo = new()
        {
            OperationName = operationName,
            ConnectId = connectId,
            StartTime = DateTime.UtcNow,
            MaxRetries = maxRetries,
            UniqueKey = operationKey,
            CurrentRetryCount = 1,
            IsExhausted = false
        };

        _activeRetryOperations.TryAdd(operationKey, operationInfo);
        Log.Debug(
            "🟡 STARTED TRACKING: Operation {OperationName} on ConnectId {ConnectId} - Key: {OperationKey}. Active operations: {ActiveCount}",
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
            return;

        try
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-OperationTimeoutMinutes);
            List<string> abandonedKeys = new();
            foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
            {
                if (operation.StartTime < cutoff)
                {
                    abandonedKeys.Add(operation.UniqueKey);
                }
            }

            foreach (string key in abandonedKeys)
            {
                if (_activeRetryOperations.TryRemove(key, out RetryOperationInfo? operation))
                {
                    Log.Debug("🧹 CLEANUP: Removed abandoned operation {OperationName} after {Minutes} minutes",
                        operation.OperationName, OperationTimeoutMinutes);
                }
            }

            if (abandonedKeys.Count > 0)
            {
                Log.Information("🧹 CLEANUP: Removed {Count} abandoned operations from tracking", abandonedKeys.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during operation cleanup");
        }
    }

    private IAsyncPolicy<Result<TResponse, NetworkFailure>> CreateTypedRetryPolicy<TResponse>(
        int maxRetries,
        string operationKey,
        string operationName,
        uint connectId)
    {
        IEnumerable<TimeSpan> rawDelays = _configuration.UseAdaptiveRetry
            ? Backoff.DecorrelatedJitterBackoffV2(
                medianFirstRetryDelay: _configuration.InitialRetryDelay,
                retryCount: maxRetries,
                fastFirst: true)
            : Backoff.ExponentialBackoff(
                initialDelay: _configuration.InitialRetryDelay,
                retryCount: maxRetries,
                factor: 2.0,
                fastFirst: true);

        TimeSpan[] retryDelays = rawDelays.Select(delay =>
            delay > _configuration.MaxRetryDelay ? _configuration.MaxRetryDelay : delay).ToArray();

        ShouldRetryDelegate<TResponse>
            shouldRetryDelegate = RetryDecisionFactory.CreateShouldRetryDelegate<TResponse>();
        RequiresConnectionRecoveryDelegate<TResponse> connectionRecoveryDelegate =
            RetryDecisionFactory.CreateConnectionRecoveryDelegate<TResponse>();

        IAsyncPolicy<Result<TResponse, NetworkFailure>> retryPolicy = Policy
            .HandleResult<Result<TResponse, NetworkFailure>>(result => shouldRetryDelegate(result))
            .WaitAndRetryAsync(
                retryDelays,
                onRetry: (outcome, delay, retryCount, context) =>
                {
                    Log.Information(
                        "🔄 RETRY ATTEMPT: Operation {OperationName} on ConnectId {ConnectId} - Attempt {RetryCount}/{MaxRetries}, delay: {DelayMs}ms",
                        operationName, connectId, retryCount, retryDelays.Length, delay.TotalMilliseconds);

                    if (retryCount == 1)
                    {
                        StartTrackingOperation(operationName, connectId, retryDelays.Length, operationKey);
                    }
                    else
                    {
                        UpdateOperationRetryCount(operationKey, retryCount);
                    }

                    if (retryCount >= retryDelays.Length)
                    {
                        Log.Warning(
                            "🔴 RETRY DELAYS EXHAUSTED: Operation {OperationName} on ConnectId {ConnectId} - Attempt {RetryCount}/{MaxRetries} exhausted all retries",
                            operationName, connectId, retryCount, retryDelays.Length);

                        MarkOperationAsExhausted(operationKey);

                        bool allExhausted = IsGloballyExhausted();
                        if (allExhausted)
                        {
                            Log.Warning(
                                "🔴 ALL OPERATIONS EXHAUSTED: All retry operations are exhausted. Recovery will handle retry button display");

                            _uiDispatcher.Post(() =>
                            {
                                try
                                {
                                    _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.RetriesExhausted);
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
                            foreach (RetryOperationInfo operation in _activeRetryOperations.Values)
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

                                _uiDispatcher.Post(() =>
                                {
                                    try
                                    {
                                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterDisconnected);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex, "Failed to notify UI of disconnected state");
                                    }
                                });
                            }

                            Log.Debug(
                                "⏳ OTHER OPERATIONS STILL RETRYING: Not showing retry button yet. Exhausted operations: {ExhaustedCount}",
                                exhaustedCount);
                        }
                    }

                    if (connectionRecoveryDelegate(outcome.Result))
                    {
                        _ = Task.Run(() => EnsureSecrecyChannelAsync(connectId));
                    }
                });

        return retryPolicy;
    }

    private async Task EnsureSecrecyChannelAsync(uint connectId)
    {
        NetworkProvider? networkProvider = GetNetworkProvider();
        if (networkProvider == null)
        {
            Log.Debug("NetworkProvider not available for connection recovery");
            return;
        }

        try
        {
            if (networkProvider.IsConnectionHealthy(connectId))
            {
                return;
            }

            Log.Debug("Attempting to restore connection for ConnectId {ConnectId}", connectId);
            Result<bool, NetworkFailure> restoreResult =
                await networkProvider.TryRestoreConnectionAsync(connectId).ConfigureAwait(false);

            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                Log.Information("Successfully restored connection for ConnectId {ConnectId}", connectId);
            }
            else if (restoreResult.IsErr)
            {
                Log.Warning("Failed to restore connection for ConnectId {ConnectId}: {Error}", connectId,
                    restoreResult.UnwrapErr().Message);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error attempting to restore connection for ConnectId {ConnectId}", connectId);
        }
    }

    private NetworkProvider? GetNetworkProvider()
    {
        try
        {
            return _lazyNetworkProvider?.Value;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get NetworkProvider instance");
            return null;
        }
    }

    private static string CreateOperationKey(string operationName, uint connectId, DateTime startTime)
    {
        return $"{operationName}_{connectId}_{startTime.Ticks}";
    }
}