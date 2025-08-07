using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Services;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Contracts.Core;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Network.Services.Queue;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Security;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Network.Core.Providers;

public sealed class NetworkProvider : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private readonly IRpcServiceManager _rpcServiceManager;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly ISecureProtocolStateStorage _secureProtocolStateStorage;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private readonly INetworkEvents _networkEvents;
    private readonly ISystemEvents _systemEvents;
    private readonly IRetryStrategy _retryStrategy;

    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();

    private readonly IConnectionStateManager _connectionStateManager;
    private readonly IOperationQueue _operationQueue;

    private const int DefaultOneTimeKeyCount = 5;

    private readonly SemaphoreSlim _secrecyChannelRecoveryLock = new(1, 1);
    private volatile bool _isSecrecyChannelConsideredHealthy;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<Result<Unit, NetworkFailure>>> _activeRequests =
        new();

    private readonly Lock _cancellationLock = new();
    private CancellationTokenSource? _connectionRecoveryCts;

    private readonly ConcurrentDictionary<uint, DateTime> _lastRecoveryAttempts = new();
    private readonly SemaphoreSlim _recoveryThrottleLock = new(1, 1);
    private readonly TimeSpan _recoveryThrottleInterval = TimeSpan.FromSeconds(10);


    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    public NetworkProvider(
        IRpcServiceManager rpcServiceManager,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        ISecureProtocolStateStorage secureProtocolStateStorage,
        IRpcMetaDataProvider rpcMetaDataProvider,
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        IRetryStrategy retryStrategy,
        IConnectionStateManager connectionStateManager,
        IOperationQueue operationQueue)
    {
        _rpcServiceManager = rpcServiceManager;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _secureProtocolStateStorage = secureProtocolStateStorage;
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _networkEvents = networkEvents;
        _systemEvents = systemEvents;
        _retryStrategy = retryStrategy;
        _connectionStateManager = connectionStateManager;
        _operationQueue = operationQueue;

        _connectionStateManager.HealthChanged
            .Where(health => health.Status is ConnectionHealthStatus.Failed or ConnectionHealthStatus.Unhealthy)
            .Subscribe(health =>
            {
                Log.Warning("Connection {ConnectId} health degraded to {Status}, initiating recovery with cancellation",
                    health.ConnectId, health.Status);
                ExecuteBackgroundTask(
                    () => InitiateConnectionRecoveryWithCancellation(health.ConnectId),
                    $"ConnectionRecovery-{health.ConnectId}");
            });

        _connectionStateManager.HealthChanged
            .Where(health => health.Status is ConnectionHealthStatus.Healthy)
            .Subscribe(health =>
            {
                Log.Information("Connection {ConnectId} became healthy, processing queued operations",
                    health.ConnectId);
                ExecuteBackgroundTask(
                    () => ProcessQueuedOperationsForConnection(health.ConnectId),
                    $"ProcessQueuedOperations-{health.ConnectId}");
            });

        Log.Information("Advanced NetworkProvider infrastructure initialized");
    }

    private CancellationToken GetConnectionRecoveryToken()
    {
        lock (_cancellationLock)
        {
            return _connectionRecoveryCts?.Token ?? CancellationToken.None;
        }
    }

    private void CancelActiveRecoveryOperations()
    {
        lock (_cancellationLock)
        {
            if (_connectionRecoveryCts != null)
            {
                Log.Information("Cancelling active recovery operations to prevent request spam");
                _connectionRecoveryCts.Cancel();
                _connectionRecoveryCts.Dispose();
            }

            _connectionRecoveryCts = new CancellationTokenSource();
        }
    }

    private void CancelActiveRequestsDuringRecovery(string reason)
    {
        if (_activeRequests.IsEmpty) return;

        Log.Information("Cancelling {Count} active requests during recovery: {Reason}",
            _activeRequests.Count, reason);

        List<string> keysToCancel = _activeRequests.Keys.ToList();
        foreach (string key in keysToCancel)
        {
            CancelAndRemoveActiveRequest(key, reason);
        }
    }

    private void CancelAndRemoveActiveRequest(string requestKey, string reason)
    {
        if (_activeRequests.TryRemove(requestKey, out TaskCompletionSource<Result<Unit, NetworkFailure>>? tcs))
        {
            try
            {
                tcs.SetResult(Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType(reason)));
                Log.Debug("Cancelled active request {RequestKey}: {Reason}", requestKey, reason);
            }
            catch (InvalidOperationException)
            {
                // Task was already completed, ignore
            }
        }
    }

    public ApplicationInstanceSettings ApplicationInstanceSettings =>
        _applicationInstanceSettings.Value!;

    public void ClearConnection(uint connectId)
    {
        if (!_connections.TryRemove(connectId, out var system)) return;
        system.Dispose();
        _connectionStateManager.RemoveConnection(connectId);
        _operationQueue.ClearConnectionQueue(connectId);
        Log.Information("Cleared connection {ConnectId} from advanced cache and monitoring", connectId);
    }

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(DefaultOneTimeKeyCount).Unwrap();
        EcliptixProtocolSystem protocolSystem = new(identityKeys);

        protocolSystem.SetEventHandler(this);

        _connections.TryAdd(connectId, protocolSystem);

        ConnectionHealth initialHealth = new()
        {
            ConnectId = connectId,
            Status = ConnectionHealthStatus.Healthy,
            LastHealthCheck = DateTime.UtcNow
        };
        _connectionStateManager.RegisterConnection(connectId, initialHealth);

        Guid appInstanceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);
        string culture = applicationInstanceSettings.Culture;

        _rpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    public void SetSecrecyChannelAsUnhealthy()
    {
        _isSecrecyChannelConsideredHealthy = false;
        _networkEvents.InitiateChangeState(NetworkStatusChangedEvent.New(NetworkStatus.DataCenterDisconnected));
    }

    public async Task<Result<Unit, NetworkFailure>> RestoreSecrecyChannelAsync()
    {
        await _secrecyChannelRecoveryLock.WaitAsync();
        try
        {
            if (_isSecrecyChannelConsideredHealthy)
            {
                Log.Information("Session was already recovered by another thread. Skipping redundant recovery");
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            Log.Information("Starting advanced session recovery process...");
            Result<Unit, NetworkFailure> result = await PerformAdvancedRecoveryLogic();

            _isSecrecyChannelConsideredHealthy = result.IsOk;
            if (result.IsErr)
            {
                Log.Error(result.UnwrapErr().Message, "Advanced session recovery failed.");
            }

            return result;
        }
        finally
        {
            _secrecyChannelRecoveryLock.Release();
        }
    }

    private async Task<Result<Unit, NetworkFailure>> PerformAdvancedRecoveryLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);
        _connections.TryRemove(connectId, out _);

        string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
        Result<byte[], SecureStorageFailure> stateResult =
            await _secureProtocolStateStorage.LoadStateAsync(userId);

        bool restorationSucceeded = false;
        if (stateResult.IsOk)
        {
            try
            {
                byte[] stateBytes = stateResult.Unwrap();
                EcliptixSecrecyChannelState state = EcliptixSecrecyChannelState.Parser.ParseFrom(stateBytes);
                Result<bool, NetworkFailure> restoreResult =
                    await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!);
                restorationSucceeded = restoreResult.IsOk && restoreResult.Unwrap();
                if (restorationSucceeded)
                {
                    Log.Information("Successfully restored existing connection state for {ConnectId}", connectId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse stored state for connection {ConnectId}, establishing new connection",
                    connectId);
                restorationSucceeded = false;
            }
        }

        if (!restorationSucceeded)
        {
            Result<EcliptixSecrecyChannelState, NetworkFailure> newResult =
                await EstablishSecrecyChannelAsync(connectId);
            restorationSucceeded = newResult.IsOk;
            if (restorationSucceeded)
            {
                Log.Information("Successfully established new connection for {ConnectId}", connectId);
            }
        }

        if (restorationSucceeded)
        {
            Log.Information("Advanced session restoration completed for {ConnectId}", connectId);
            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        Log.Warning("Advanced restoration failed, falling back to reconnection");
        Result<Unit, NetworkFailure> reconnectionResult = await PerformReconnectionLogic();
        if (reconnectionResult.IsErr)
        {
            return reconnectionResult;
        }

        Log.Information("Session successfully re-established via reconnection");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType) =>
        Helpers.ComputeUniqueConnectId(
            applicationInstanceSettings.AppInstanceId.Span,
            applicationInstanceSettings.DeviceId.Span,
            pubKeyExchangeType);

    public async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicates = false, CancellationToken token = default)
    {
        if (_disposed)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("NetworkProvider is disposed"));
        }

        string requestKey =
            $"{connectId}_{serviceType}_{Convert.ToHexString(plainBuffer)[..Math.Min(16, plainBuffer.Length)]}";

        bool shouldAllowDuplicates = allowDuplicates || ShouldAllowDuplicateRequests(serviceType);

        if (!shouldAllowDuplicates && _activeRequests.TryGetValue(requestKey,
                out TaskCompletionSource<Result<Unit, NetworkFailure>>? existingRequest))
        {
            CancellationToken recoveryToken = GetConnectionRecoveryToken();
            if (recoveryToken.IsCancellationRequested)
            {
                Log.Debug("Cancelling duplicate request for {ServiceType} due to active recovery", serviceType);
                CancelAndRemoveActiveRequest(requestKey, "Request cancelled due to connection recovery");
            }
            else
            {
                Log.Debug("Duplicate request detected for {ServiceType}, waiting for existing request to complete",
                    serviceType);
                return await existingRequest.Task;
            }
        }

        TaskCompletionSource<Result<Unit, NetworkFailure>> requestTcs = new();
        if (!_activeRequests.TryAdd(requestKey, requestTcs))
        {
            if (_activeRequests.TryGetValue(requestKey,
                    out TaskCompletionSource<Result<Unit, NetworkFailure>>? concurrentRequest))
            {
                return await concurrentRequest.Task;
            }
        }

        try
        {
            CancellationToken recoveryToken = GetConnectionRecoveryToken();
            if (recoveryToken.IsCancellationRequested)
            {
                Log.Debug("Request for {ServiceType} cancelled due to active recovery", serviceType);
                requestTcs.SetResult(Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Request cancelled during connection recovery")));
                return await requestTcs.Task;
            }

            using CancellationTokenSource combinedCts =
                CancellationTokenSource.CreateLinkedTokenSource(token, recoveryToken);
            DateTime startTime = DateTime.UtcNow;

            if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
            {
                requestTcs.SetResult(Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Connection not found")));
                return await requestTcs.Task;
            }

            ServiceRequest request = BuildRequest(protocolSystem, serviceType, plainBuffer, flowType, connectId);
            Result<Unit, NetworkFailure> result =
                await SendRequestAsync(protocolSystem, request, onCompleted, combinedCts.Token, connectId);

            if (result.IsErr)
            {
                NetworkFailure failure = result.UnwrapErr();
                if (failure.Message.Contains("Decrypt failed") ||
                    failure.Message.Contains("desync") ||
                    failure.Message.Contains("rekey"))
                {
                    Log.Warning("Cryptographic desync detected for connection {ConnectId}, checking recovery throttle",
                        connectId);
                    if (!ShouldThrottleRecovery(connectId))
                    {
                        _lastRecoveryAttempts.AddOrUpdate(connectId, DateTime.UtcNow, (_, _) => DateTime.UtcNow);

                        CancelActiveRecoveryOperations();
                        CancelActiveRequestsDuringRecovery("Server-side failure detected, cancelling active requests");
                        ExecuteBackgroundTask(
                            PerformAdvancedRecoveryLogic,
                            $"CryptographicRecovery-{connectId}");
                    }
                }
            }

            TimeSpan duration = DateTime.UtcNow - startTime;
            OperationType operationType = MapServiceToOperationType(serviceType);

            _connectionStateManager.UpdateConnectionHealth(connectId, result);

            if (result.IsErr && ShouldQueueFailedOperation(result.UnwrapErr(), serviceType))
            {
                await QueueFailedOperationForLaterRetry(connectId, serviceType, plainBuffer, flowType, onCompleted,
                    result.UnwrapErr());
                Log.Information(
                    "Operation {ServiceType} queued for later retry when server becomes healthy for connection {ConnectId}",
                    serviceType, connectId);

                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            requestTcs.SetResult(result);
            return result;
        }
        finally
        {
            _activeRequests.TryRemove(requestKey, out _);
        }
    }

    private bool ShouldThrottleRecovery(uint connectId)
    {
        if (!_lastRecoveryAttempts.TryGetValue(connectId, out DateTime lastRecovery))
        {
            return false;
        }

        return DateTime.UtcNow - lastRecovery < _recoveryThrottleInterval;
    }

    private async Task InitiateConnectionRecoveryWithCancellation(uint connectId)
    {
        try
        {
            CancelActiveRecoveryOperations();
            CancelActiveRequestsDuringRecovery("Connection recovery initiated");
            _connectionStateManager.MarkConnectionRecovering(connectId);

            Log.Information("Initiating advanced recovery with cancellation for connection {ConnectId}", connectId);

            if (!_applicationInstanceSettings.HasValue)
            {
                Log.Warning("Cannot recover connection {ConnectId} - application settings not available", connectId);
                return;
            }

            // Use dedicated ISecureProtocolStateStorage for protocol state restoration
            string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
            Result<byte[], SecureStorageFailure> stateResult =
                await _secureProtocolStateStorage.LoadStateAsync(userId);

            bool restorationSuccessful = false;
            if (stateResult.IsOk)
            {
                try
                {
                    EcliptixSecrecyChannelState state =
                        EcliptixSecrecyChannelState.Parser.ParseFrom(stateResult.Unwrap());
                    Result<bool, NetworkFailure> restoreResult =
                        await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!);
                    restorationSuccessful = restoreResult.IsOk && restoreResult.Unwrap();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse stored state for connection {ConnectId}", connectId);
                    restorationSuccessful = false;
                }
            }
            else
            {
                // No state found, establish new connection
                Result<EcliptixSecrecyChannelState, NetworkFailure> newResult =
                    await EstablishSecrecyChannelAsync(connectId);
                restorationSuccessful = newResult.IsOk;
            }

            if (restorationSuccessful)
            {
                Log.Information("Advanced recovery completed for connection {ConnectId}", connectId);
            }
            else
            {
                Log.Warning("Advanced recovery failed for connection {ConnectId}, falling back to reconnection",
                    connectId);
                await PerformReconnectionLogic();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during connection recovery for {ConnectId}", connectId);
        }
    }

    private static OperationType MapServiceToOperationType(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.RegisterAppDevice => OperationType.RegisterDevice,
            RpcServiceType.EstablishSecrecyChannel => OperationType.EstablishChannel,
            RpcServiceType.RestoreSecrecyChannel => OperationType.RestoreChannel,
            _ => OperationType.SendMessage
        };
    }

    //TODO: current code will be reworked. O.Melnychenko 07.08.2025
    private static bool ShouldQueueFailedOperation(NetworkFailure failure, RpcServiceType serviceType)
    {
        string message = failure.Message.ToLowerInvariant();

        if (message.Contains("network") || message.Contains("connection") ||
            message.Contains("unreachable") || message.Contains("timeout") ||
            message.Contains("server") || message.Contains("internal") ||
            message.Contains("service unavailable") || message.Contains("deadline"))
        {
            Log.Debug("Operation {ServiceType} eligible for queuing due to connectivity failure: {Error}",
                serviceType, failure.Message);
            return true;
        }

        if (message.Contains("auth") || message.Contains("unauthorized") ||
            message.Contains("forbidden") || message.Contains("bad request") ||
            message.Contains("invalid"))
        {
            Log.Debug("Operation {ServiceType} NOT queued - permanent error: {Error}",
                serviceType, failure.Message);
            return false;
        }

        Log.Debug("Operation {ServiceType} queued for unknown error (conservative approach): {Error}",
            serviceType, failure.Message);
        return false;
    }

    private async Task QueueFailedOperationForLaterRetry(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        NetworkFailure originalFailure)
    {
        try
        {
            QueuedOperation queuedOperation = new()
            {
                ConnectId = connectId,
                Type = MapServiceToOperationType(serviceType),
                Priority = DetermineOperationPriority(serviceType),
                IsPersistent = true,
                ExpiresAfter = TimeSpan.FromHours(24),
                ExecuteAsync = async cancellationToken =>
                {
                    Log.Debug("Executing queued operation {ServiceType} for connection {ConnectId}",
                        serviceType, connectId);

                    if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                    {
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.InvalidRequestType("Connection not found during queued execution"));
                    }

                    ServiceRequest request =
                        BuildRequest(protocolSystem, serviceType, plainBuffer, flowType, connectId);
                    return await SendRequestAsync(protocolSystem, request, onCompleted, cancellationToken, connectId);
                },
                Metadata = new Dictionary<string, object>
                {
                    ["ServiceType"] = serviceType.ToString(),
                    ["FlowType"] = flowType.ToString(),
                    ["OriginalFailure"] = originalFailure.Message,
                    ["QueuedAt"] = DateTime.UtcNow
                }
            };

            Result<string, NetworkFailure> queueResult = _operationQueue.EnqueueOperation(queuedOperation);
            if (queueResult.IsErr)
            {
                Log.Warning("Failed to queue operation {ServiceType} for connection {ConnectId}: {Error}",
                    serviceType, connectId, queueResult.UnwrapErr().Message);
            }
            else
            {
                Log.Information(
                    "Successfully queued operation {ServiceType} with ID {OperationId} for connection {ConnectId}",
                    serviceType, queueResult.Unwrap(), connectId);
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error queueing operation {ServiceType} for connection {ConnectId}", serviceType, connectId);
        }
    }

    private static OperationPriority DetermineOperationPriority(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.EstablishSecrecyChannel => OperationPriority.Critical,
            RpcServiceType.RestoreSecrecyChannel => OperationPriority.Critical,
            RpcServiceType.RegisterAppDevice => OperationPriority.High,
            _ => OperationPriority.Normal
        };
    }

    private static bool ShouldAllowDuplicateRequests(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.InitiateVerification => true, // OTP resend operations
            RpcServiceType.ValidatePhoneNumber => true, // Phone validation retries
            _ => false
        };
    }

    private async Task ProcessQueuedOperationsForConnection(uint connectId)
    {
        try
        {
            Log.Information("Starting to process queued operations for healthy connection {ConnectId}", connectId);

            IEnumerable<QueuedOperation> pendingOperations = _operationQueue.GetPendingOperations(connectId)
                .OrderByDescending(op => op.Priority)
                .ThenBy(op => op.QueuedAt);

            int totalOperations = pendingOperations.Count();
            if (totalOperations == 0)
            {
                Log.Debug("No queued operations found for connection {ConnectId}", connectId);
                return;
            }

            Log.Information(
                "Found {OperationCount} queued operations for connection {ConnectId}, processing in priority order",
                totalOperations, connectId);

            int processed = 0;
            int succeeded = 0;
            int failed = 0;

            foreach (QueuedOperation operation in pendingOperations)
            {
                try
                {
                    Log.Debug(
                        "Processing queued operation {OperationId} of type {OperationType} for connection {ConnectId}",
                        operation.Id, operation.Type, connectId);

                    Result<Unit, NetworkFailure> result = await operation.ExecuteAsync(CancellationToken.None);
                    processed++;

                    if (result.IsOk)
                    {
                        succeeded++;
                        Log.Debug("Queued operation {OperationId} executed successfully", operation.Id);
                    }
                    else
                    {
                        failed++;
                        Log.Warning("Queued operation {OperationId} failed during execution: {Error}",
                            operation.Id, result.UnwrapErr().Message);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    processed++;
                    Log.Error(ex, "Exception processing queued operation {OperationId} for connection {ConnectId}",
                        operation.Id, connectId);
                }

                await Task.Delay(100);
            }

            Log.Information("Completed processing queued operations for connection {ConnectId}: " +
                            "Total={Total}, Processed={Processed}, Succeeded={Succeeded}, Failed={Failed}",
                connectId, totalOperations, processed, succeeded, failed);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error processing queued operations for connection {ConnectId}", connectId);
        }
    }

    public async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelAsync(
        EcliptixSecrecyChannelState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);
        }

        _rpcMetaDataProvider.SetAppInfo(Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId), applicationInstanceSettings.Culture);

        RestoreSecrecyChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse>.New(
                ServiceFlowType.Single,
                RpcServiceType.RestoreSecrecyChannel,
                request);

        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

        Result<RestoreSecrecyChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse =
            await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                () => _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                    serviceRequest),
                "RestoreSecrecyChannel",
                ecliptixSecrecyChannelState.ConnectId,
                cancellationToken: combinedCts.Token);

        if (restoreAppDeviceSecrecyChannelResponse.IsErr)
            return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());

        RestoreSecrecyChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        if (response.Status == RestoreSecrecyChannelResponse.Types.RestoreStatus.SessionResumed)
        {
            SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

            return Result<bool, NetworkFailure>.Ok(true);
        }

        Log.Information("Session not found on server (status: {Status}), will establish new channel", response.Status);
        return Result<bool, NetworkFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSecrecyChannelState, NetworkFailure>> EstablishSecrecyChannelAsync(
        uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<EcliptixSecrecyChannelState, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Connection not found"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, PubKeyExchangeType.DataCenterEphemeralConnect);

        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> action =
            SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange>.New(
                ServiceFlowType.Single,
                RpcServiceType.EstablishSecrecyChannel,
                pubKeyExchangeRequest.Unwrap());

        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

        Result<PubKeyExchange, NetworkFailure> establishAppDeviceSecrecyChannelResult =
            await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                () => _rpcServiceManager.EstablishAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents, action),
                "EstablishSecrecyChannel",
                connectId,
                cancellationToken: combinedCts.Token);

        PubKeyExchange peerPubKeyExchange = establishAppDeviceSecrecyChannelResult.Unwrap();

        protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange);

        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection connection = protocolSystem.GetConnection();

        Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> ecliptixSecrecyChannelStateResult =
            idKeys.ToProtoState()
                .AndThen(identityKeysProto => connection.ToProtoState()
                    .Map(ratchetStateProto => new EcliptixSecrecyChannelState
                    {
                        ConnectId = connectId,
                        IdentityKeys = identityKeysProto,
                        PeerHandshakeMessage = peerPubKeyExchange,
                        RatchetState = ratchetStateProto
                    })
                );

        return ecliptixSecrecyChannelStateResult.ToNetworkFailure();
    }

    private Result<Unit, EcliptixProtocolFailure> SyncSecrecyChannel(
        EcliptixSecrecyChannelState currentState,
        RestoreSecrecyChannelResponse peerSecrecyChannelState)
    {
        Result<EcliptixProtocolSystem, EcliptixProtocolFailure> systemResult = RecreateSystemFromState(currentState);
        if (systemResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(systemResult.UnwrapErr());
        }

        EcliptixProtocolSystem system = systemResult.Unwrap();

        system.SetEventHandler(this);

        EcliptixProtocolConnection connection = system.GetConnection();

        Result<Unit, EcliptixProtocolFailure> syncResult = connection.SyncWithRemoteState(
            peerSecrecyChannelState.SendingChainLength,
            peerSecrecyChannelState.ReceivingChainLength
        );

        if (syncResult.IsErr)
        {
            system.Dispose();
            return Result<Unit, EcliptixProtocolFailure>.Err(syncResult.UnwrapErr());
        }

        _connections.TryAdd(currentState.ConnectId, system);
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private static Result<EcliptixProtocolSystem, EcliptixProtocolFailure> RecreateSystemFromState(
        EcliptixSecrecyChannelState state)
    {
        Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> idKeysResult =
            EcliptixSystemIdentityKeys.FromProtoState(state.IdentityKeys);
        if (idKeysResult.IsErr)
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> connResult =
            EcliptixProtocolConnection.FromProtoState(state.ConnectId, state.RatchetState);

        if (!connResult.IsErr) return EcliptixProtocolSystem.CreateFrom(idKeysResult.Unwrap(), connResult.Unwrap());
        idKeysResult.Unwrap().Dispose();
        return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(connResult.UnwrapErr());
    }

    private async Task<Result<Unit, NetworkFailure>> PerformReconnectionLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Unit, NetworkFailure> establishResult = await EstablishConnectionOnly(connectId);

        if (establishResult.IsErr)
        {
            return establishResult;
        }

        Log.Information("Successfully reconnected and established new session");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> EstablishConnectionOnly(uint connectId)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        _connections.TryRemove(connectId, out _);

        InitiateEcliptixProtocolSystem(_applicationInstanceSettings.Value!, connectId);

        Result<EcliptixSecrecyChannelState, NetworkFailure> establishResult =
            await EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSecrecyChannelState secrecyChannelState = establishResult.Unwrap();

        // Save protocol state using dedicated secure storage
        string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
        Result<Unit, SecureStorageFailure> saveResult = await _secureProtocolStateStorage.SaveStateAsync(
            secrecyChannelState.ToByteArray(), userId);
            
        if (saveResult.IsOk)
        {
            Log.Information("Protocol state saved securely for connection {ConnectId}", connectId);
        }
        else
        {
            Log.Warning("Failed to save protocol state: {Error}", saveResult.UnwrapErr().Message);
        }

        // Keep timestamp in application storage for backwards compatibility
        string timestampKey = $"{connectId}_timestamp";
        await _applicationSecureStorageProvider.StoreAsync(timestampKey,
            BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));

        Log.Information("Successfully established new connection");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private ServiceRequest BuildRequest(
        EcliptixProtocolSystem protocolSystem,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        uint connectId)
    {
        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);

        CipherPayload cipherPayload = outboundPayload.Unwrap();

        // State callbacks removed - using unified ISecureStorageProvider persistence

        return ServiceRequest.New(flowType, serviceType, cipherPayload, []);
    }

    private async Task<Result<Unit, NetworkFailure>> SendRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token,
        uint connectId = 0)
    {
        Log.Debug("SendRequestAsync - ServiceType: {ServiceType}", request.RpcServiceMethod);
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            Log.Warning("InvokeServiceRequestAsync failed: {Error}", invokeResult.UnwrapErr().Message);
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        switch (flow)
        {
            case RpcFlow.SingleCall singleCall:
                Result<CipherPayload, NetworkFailure> callResult = await singleCall.Result;
                if (callResult.IsErr)
                {
                    Log.Warning("RPC call failed: {Error}", callResult.UnwrapErr().Message);
                    return Result<Unit, NetworkFailure>.Err(callResult.UnwrapErr());
                }

                CipherPayload inboundPayload = callResult.Unwrap();
                Log.Debug("Received CipherPayload - Nonce: {Nonce}, Size: {Size}",
                    Convert.ToHexString(inboundPayload.Nonce.ToByteArray()), inboundPayload.Cipher.Length);
                Result<byte[], EcliptixProtocolFailure> decryptedData =
                    protocolSystem.ProcessInboundMessage(inboundPayload);
                if (decryptedData.IsErr)
                {
                    Log.Warning("Decryption failed: {Error}", decryptedData.UnwrapErr().Message);
                    return Result<Unit, NetworkFailure>.Err(decryptedData.UnwrapErr().ToNetworkFailure());
                }

                Log.Debug("Successfully decrypted response");

                // State callbacks removed - using unified ISecureStorageProvider persistence

                Result<Unit, NetworkFailure> callbackOutcome = await onCompleted(decryptedData.Unwrap());
                if (callbackOutcome.IsErr) return callbackOutcome;
                break;

            case RpcFlow.InboundStream inboundStream:
                await foreach (Result<CipherPayload, NetworkFailure> streamItem in
                               inboundStream.Stream.WithCancellation(token))
                {
                    if (streamItem.IsErr) continue;

                    CipherPayload streamPayload = streamItem.Unwrap();
                    Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                        protocolSystem.ProcessInboundMessage(streamPayload);
                    if (streamDecryptedData.IsErr) continue;

                    Result<Unit, NetworkFailure> streamCallbackOutcome =
                        await onCompleted(streamDecryptedData.Unwrap());
                    if (streamCallbackOutcome.IsErr)
                    {
                        Log.Warning("Stream callback failed: {Error}", streamCallbackOutcome.UnwrapErr());
                    }
                }

                break;

            case RpcFlow.OutboundSink _:
            case RpcFlow.BidirectionalStream _:
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Unsupported stream type"));
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private volatile bool _disposed;
    private readonly object _disposeLock = new();
    private readonly List<Task> _backgroundTasks = [];
    private readonly Lock _backgroundTasksLock = new();

    private void ExecuteBackgroundTask(Func<Task> taskFactory, string taskName)
    {
        if (_disposed) return;

        Task task = Task.Run(async () =>
        {
            try
            {
                await taskFactory();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in background task: {TaskName}", taskName);
            }
        });

        TrackBackgroundTask(task);
    }

    private void TrackBackgroundTask(Task task)
    {
        lock (_backgroundTasksLock)
        {
            _backgroundTasks.Add(task);

            _backgroundTasks.RemoveAll(t => t.IsCompleted);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _connectionStateManager?.Dispose();
                _operationQueue?.Dispose();
                // Event adapter removed - using direct INetworkEvents instead

                _activeRequests.Clear();
                _lastRecoveryAttempts.Clear();
                _recoveryThrottleLock?.Dispose();

                Task[] backgroundTasksToWait;
                lock (_backgroundTasksLock)
                {
                    backgroundTasksToWait = _backgroundTasks.Where(t => !t.IsCompleted).ToArray();
                }

                if (backgroundTasksToWait.Length > 0)
                {
                    try
                    {
                        Task.WaitAll(backgroundTasksToWait, TimeSpan.FromSeconds(5));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error waiting for background tasks during disposal");
                    }
                }

                List<KeyValuePair<uint, EcliptixProtocolSystem>> connectionsToDispose = new(_connections);
                _connections.Clear();

                foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in connectionsToDispose)
                {
                    try
                    {
                        connection.Value?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error disposing protocol system for connection {ConnectId}", connection.Key);
                    }
                }

                lock (_cancellationLock)
                {
                    _connectionRecoveryCts?.Dispose();
                    _connectionRecoveryCts = null;
                }

                Log.Information("Advanced NetworkProvider disposed safely");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during NetworkProvider disposal");
            }
        }
    }

    public void OnDhRatchetPerformed(uint connectId, bool isSending, uint newIndex)
    {
        Task.Run(async () =>
        {
            try
            {
                Log.Information(
                    "DH Ratchet performed - ConnectId: {ConnectId}, Type: {Type}, Index: {Index}. Saving protocol state...",
                    connectId, isSending ? "Sending" : "Receiving", newIndex);

                if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                {
                    try
                    {
                        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
                        EcliptixProtocolConnection connection = protocolSystem.GetConnection();

                        Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
                        Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

                        if (idKeysStateResult.IsOk && ratchetStateResult.IsOk)
                        {
                            EcliptixSecrecyChannelState state = new()
                            {
                                ConnectId = connectId,
                                IdentityKeys = idKeysStateResult.Unwrap(),
                                RatchetState = ratchetStateResult.Unwrap()
                            };

                            // Save protocol state using dedicated secure storage
                            string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
                            Result<Unit, SecureStorageFailure> saveResult = await _secureProtocolStateStorage.SaveStateAsync(
                                state.ToByteArray(), userId);
                                
                            if (saveResult.IsOk)
                            {
                                Log.Information(
                                    "Protocol state saved successfully after {ChainType} chain rotation - ConnectId: {ConnectId}, Index: {Index}",
                                    isSending ? "sending" : "receiving", connectId, newIndex);
                            }
                            else
                            {
                                Log.Warning("Failed to save protocol state after DH ratchet: {Error}", saveResult.UnwrapErr().Message);
                            }

                            // Keep timestamp in application storage for backwards compatibility
                            string timestampKey = $"{connectId}_timestamp";
                            await _applicationSecureStorageProvider.StoreAsync(timestampKey,
                                BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));
                        }
                        else
                        {
                            Log.Error(
                                "Failed to create protocol state after chain rotation - ConnectId: {ConnectId}, IdKeysError: {IdKeysError}, RatchetError: {RatchetError}",
                                connectId,
                                idKeysStateResult.IsErr ? idKeysStateResult.UnwrapErr().ToString() : "OK",
                                ratchetStateResult.IsErr ? ratchetStateResult.UnwrapErr().ToString() : "OK");
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Log.Error(innerEx,
                            "Exception creating protocol state after chain rotation - ConnectId: {ConnectId}",
                            connectId);
                    }
                }
                else
                {
                    Log.Warning("Protocol system not found for chain rotation state save - ConnectId: {ConnectId}",
                        connectId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving protocol state after chain rotation - ConnectId: {ConnectId}", connectId);
            }
        });
    }

    public void OnChainSynchronized(uint connectId, uint localLength, uint remoteLength)
    {
        Task.Run(async () =>
        {
            try
            {
                Log.Information(
                    "Chain synchronized - ConnectId: {ConnectId}, Local: {Local}, Remote: {Remote}. Saving protocol state...",
                    connectId, localLength, remoteLength);

                if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                {
                    try
                    {
                        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
                        EcliptixProtocolConnection connection = protocolSystem.GetConnection();

                        Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
                        Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

                        if (idKeysStateResult.IsOk && ratchetStateResult.IsOk)
                        {
                            EcliptixSecrecyChannelState state = new()
                            {
                                ConnectId = connectId,
                                IdentityKeys = idKeysStateResult.Unwrap(),
                                RatchetState = ratchetStateResult.Unwrap()
                            };

                            // Save protocol state using dedicated secure storage
                            string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
                            Result<Unit, SecureStorageFailure> saveResult = await _secureProtocolStateStorage.SaveStateAsync(
                                state.ToByteArray(), userId);
                                
                            if (saveResult.IsOk)
                            {
                                Log.Information(
                                    "Protocol state saved successfully after chain synchronization - ConnectId: {ConnectId}",
                                    connectId);
                            }
                            else
                            {
                                Log.Warning("Failed to save protocol state after chain sync: {Error}", saveResult.UnwrapErr().Message);
                            }

                            // Keep timestamp in application storage for backwards compatibility
                            string timestampKey = $"{connectId}_timestamp";
                            await _applicationSecureStorageProvider.StoreAsync(timestampKey,
                                BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));
                        }
                    }
                    catch (Exception innerEx)
                    {
                        Log.Error(innerEx,
                            "Exception creating protocol state after chain synchronization - ConnectId: {ConnectId}",
                            connectId);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving protocol state after chain synchronization - ConnectId: {ConnectId}",
                    connectId);
            }
        });
    }

    public void OnMessageProcessed(uint connectId, uint messageIndex, bool hasSkippedKeys)
    {
        // For performance, we don't save state after every message
        // Only save after DH ratchets and chain synchronization
        Log.Debug(
            "Message processed - ConnectId: {ConnectId}, Index: {Index}, SkippedKeys: {SkippedKeys}",
            connectId, messageIndex, hasSkippedKeys);
    }
}