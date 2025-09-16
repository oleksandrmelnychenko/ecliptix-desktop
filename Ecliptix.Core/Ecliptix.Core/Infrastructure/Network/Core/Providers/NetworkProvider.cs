using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.State;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

public sealed class NetworkProvider : INetworkProvider, IDisposable, IProtocolEventHandler
{
    private readonly IRpcServiceManager _rpcServiceManager;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly ISecureProtocolStateStorage _secureProtocolStateStorage;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private readonly INetworkEventService _networkEvents;
    private readonly ISystemEventService _systemEvents;
    private readonly IRetryStrategy _retryStrategy;
    private readonly IConnectionStateManager _connectionStateManager;
    private readonly IPendingRequestManager _pendingRequestManager;

    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();
    private readonly ConcurrentDictionary<uint, CancellationTokenSource> _activeStreams = new();

    private const int DefaultOneTimeKeyCount = 5;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlightRequests = new();

    private readonly Lock _cancellationLock = new();
    private CancellationTokenSource? _connectionRecoveryCts;

    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _channelGates = new();

    private readonly SemaphoreSlim _retryPendingRequestsGate = new(1, 1);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _logicalOperationGates = new();

    private readonly ConcurrentDictionary<uint, DateTime> _lastRecoveryAttempts = new();
    private readonly TimeSpan _recoveryThrottleInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, DateTime> _lastRequestAttempts = new();
    private readonly TimeSpan _requestDebounceInterval = TimeSpan.FromMilliseconds(500);

    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    private int _outageState;
    private readonly Lock _outageLock = new();
    private TaskCompletionSource<bool> _outageRecoveredTcs = CreateOutageTcs();

    private SystemState _currentSystemState = SystemState.Running;

    private static TaskCompletionSource<bool> CreateOutageTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _disposed;
    private readonly object _disposeLock = new();
    private readonly List<Task> _backgroundTasks = [];
    private readonly Lock _backgroundTasksLock = new();

    public NetworkProvider(
        IRpcServiceManager rpcServiceManager,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        ISecureProtocolStateStorage secureProtocolStateStorage,
        IRpcMetaDataProvider rpcMetaDataProvider,
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        IRetryStrategy retryStrategy,
        IConnectionStateManager connectionStateManager,
        IPendingRequestManager pendingRequestManager)
    {
        _rpcServiceManager = rpcServiceManager;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _secureProtocolStateStorage = secureProtocolStateStorage;
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _networkEvents = networkEvents;
        _systemEvents = systemEvents;
        _retryStrategy = retryStrategy;
        _connectionStateManager = connectionStateManager;
        _pendingRequestManager = pendingRequestManager;

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
    }


    private readonly Lock _appInstanceSetterLock = new();

    public void SetCountry(string country)
    {
        lock (_appInstanceSetterLock)
        {
            if (_applicationInstanceSettings.Value != null)
                _applicationInstanceSettings.Value.Country = country;
        }
    }

    public ApplicationInstanceSettings ApplicationInstanceSettings =>
        _applicationInstanceSettings.Value!;

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType)
    {
        Guid appInstanceGuid = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceGuid = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);

        string appInstanceIdString = appInstanceGuid.ToString();
        string deviceIdString = deviceGuid.ToString();

        uint connectId = Helpers.ComputeUniqueConnectId(
            appInstanceIdString,
            deviceIdString, pubKeyExchangeType);


        return connectId;
    }

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(DefaultOneTimeKeyCount).Unwrap();

        PubKeyExchangeType exchangeType = DetermineExchangeTypeFromConnectId(applicationInstanceSettings, connectId);
        RatchetConfig config = GetRatchetConfigForExchangeType(exchangeType);

        Log.Information(
            "NetworkProvider: Creating protocol with config for exchange type {ExchangeType} - DH every {Messages} messages",
            exchangeType, config.DhRatchetEveryNMessages);

        EcliptixProtocolSystem protocolSystem = new(identityKeys, config);

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
        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? "en-US"
            : applicationInstanceSettings.Culture;

        _rpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId, culture);
    }

    public void ClearConnection(uint connectId)
    {
        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? system)) return;
        system.Dispose();
        _connectionStateManager.RemoveConnection(connectId);
    }

    public async Task<Result<Unit, NetworkFailure>> ExecuteUnaryRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicates = false,
        CancellationToken token = default,
        bool waitForRecovery = true)
    {
        return await ExecuteServiceRequestInternalAsync(
            connectId, serviceType, plainBuffer, ServiceFlowType.Single,
            onCompleted, allowDuplicates, token, waitForRecovery);
    }

    public async Task<Result<Unit, NetworkFailure>> ExecuteReceiveStreamRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        bool allowDuplicates = false,
        CancellationToken token = default)
    {
        return await ExecuteServiceRequestInternalAsync(
            connectId, serviceType, plainBuffer, ServiceFlowType.ReceiveStream,
            onStreamItem, allowDuplicates, token);
    }

    private async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequestInternalAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicates = false,
        CancellationToken token = default,
        bool waitForRecovery = true)
    {
        string requestKey;
        if (serviceType is RpcServiceType.OpaqueSignInInitRequest or RpcServiceType.OpaqueSignInCompleteRequest)
        {
            requestKey = $"{connectId}_{serviceType}_auth_operation";
        }
        else
        {
            string hex = Convert.ToHexString(plainBuffer);
            string prefix = hex[..Math.Min(hex.Length, 16)];
            requestKey = $"{connectId}_{serviceType}_{prefix}";
        }

        SystemState currentState = _currentSystemState;
        if (currentState == SystemState.Recovering)
        {
            if (IsUserInitiatedRequest(serviceType))
            {
                if (!waitForRecovery)
                {
                    Log.Information(
                        "🚫 REQUEST BLOCKED: User request '{ServiceType}' blocked during recovery state (no wait)",
                        serviceType);
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding("System is recovering, please wait"));
                }

                Log.Information("⏳ REQUEST WAITING: User request '{ServiceType}' will wait for recovery completion",
                    serviceType);
            }

            if (IsRecoveryRequest(serviceType))
            {
                Log.Information("✅ RECOVERY REQUEST: Allowing recovery request '{ServiceType}' during recovery state",
                    serviceType);
            }
        }

        if (!waitForRecovery)
        {
            DateTime now = DateTime.UtcNow;
            string debounceKey = $"{connectId}_{serviceType}";
            if (_lastRequestAttempts.TryGetValue(debounceKey, out DateTime lastAttempt))
            {
                TimeSpan timeSinceLastRequest = now - lastAttempt;
                if (timeSinceLastRequest < _requestDebounceInterval)
                {
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType("Request too frequent, please wait"));
                }
            }

            _lastRequestAttempts.AddOrUpdate(debounceKey, now, (_, _) => now);
        }

        bool shouldAllowDuplicates = allowDuplicates || ShouldAllowDuplicateRequests(serviceType);
        CancellationTokenSource perRequestCts = new();
        if (!shouldAllowDuplicates && !_inFlightRequests.TryAdd(requestKey, perRequestCts))
        {
            perRequestCts.Dispose();
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Duplicate request rejected"));
        }

        using CancellationTokenSource linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(token, perRequestCts.Token);
        CancellationToken operationToken = linkedCts.Token;

        try
        {
            await WaitForOutageRecoveryAsync(operationToken, waitForRecovery).ConfigureAwait(false);

            if (Volatile.Read(ref _outageState) == 0 && currentState == SystemState.Recovering &&
                IsUserInitiatedRequest(serviceType))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
                });
            }

            string operationName = $"{serviceType}";
            Result<Unit, NetworkFailure> networkResult = await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                operation: async () =>
                {
                    operationToken.ThrowIfCancellationRequested();

                    if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                    {
                        NetworkFailure noConnectionFailure = NetworkFailure.DataCenterNotResponding(
                            "Connection unavailable - server may be recovering");

                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ServerShutdown);

                        return Result<Unit, NetworkFailure>.Err(noConnectionFailure);
                    }

                    uint secondLogicalOperationId = GenerateLogicalOperationId(connectId, serviceType, plainBuffer);

                    Result<ServiceRequest, NetworkFailure> requestResult =
                        BuildRequestWithId(protocolSystem, secondLogicalOperationId, serviceType, plainBuffer,
                            flowType);
                    if (requestResult.IsErr)
                    {
                        NetworkFailure buildFailure = requestResult.UnwrapErr();
                        if (FailureClassification.IsServerShutdown(buildFailure))
                            EnterOutage(buildFailure.Message, connectId);

                        return Result<Unit, NetworkFailure>.Err(buildFailure);
                    }

                    ServiceRequest request = requestResult.Unwrap();
                    uint originalReqId = request.ReqId;

                    string logicalOperationKey = $"{connectId}:op:{originalReqId}";

                    try
                    {
                        Result<Unit, NetworkFailure> result = await WithRequestExecutionGate(logicalOperationKey,
                            async () =>
                            {
                                return flowType switch
                                {
                                    ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, request,
                                        onCompleted,
                                        operationToken).ConfigureAwait(false),
                                    ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem,
                                        request, onCompleted, operationToken).ConfigureAwait(false),
                                    ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem,
                                        request,
                                        onCompleted, operationToken).ConfigureAwait(false),
                                    ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(
                                        protocolSystem, request, onCompleted, operationToken).ConfigureAwait(false),
                                    _ => Result<Unit, NetworkFailure>.Err(
                                        NetworkFailure.InvalidRequestType($"Unsupported flow type: {flowType}"))
                                };
                            }).ConfigureAwait(false);

                        if (!result.IsErr) return result;
                        NetworkFailure failure = result.UnwrapErr();

                        if (FailureClassification.IsServerShutdown(failure))
                        {
                            string requestId = originalReqId.ToString();
                            _pendingRequestManager.RegisterPendingRequest(requestId,
                                async () =>
                                {
                                    await ReExecuteServiceFromPlaintextAsync(connectId, serviceType, plainBuffer,
                                        flowType, onCompleted, originalReqId, token).ConfigureAwait(false);
                                });

                            EnterOutage(failure.Message, connectId);

                            if (!waitForRecovery) return result;
                            Log.Information("Request {ReqId} waiting for server recovery", originalReqId);
                            try
                            {
                                await WaitForOutageRecoveryAsync(token, true);
                                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
                            }
                            catch (OperationCanceledException)
                            {
                                return Result<Unit, NetworkFailure>.Err(
                                    NetworkFailure.DataCenterNotResponding(
                                        "Request cancelled during outage recovery wait"));
                            }
                        }
                        else if (FailureClassification.IsCryptoDesync(failure))
                        {
                            Log.Warning(
                                "Cryptographic desync detected for connection {ConnectId}, initiating recovery",
                                connectId);
                            if (ShouldThrottleRecovery(connectId)) return result;
                            _lastRecoveryAttempts.AddOrUpdate(connectId, DateTime.UtcNow,
                                (_, _) => DateTime.UtcNow);
                            ExecuteBackgroundTask(PerformAdvancedRecoveryLogic,
                                $"CryptographicRecovery-{connectId}");
                        }
                        else if (FailureClassification.IsChainRotationMismatch(failure))
                        {
                            Log.Warning(
                                "Chain rotation mismatch detected for connection {ConnectId}: {Message}. Initiating protocol resynchronization",
                                connectId, failure.Message);
                            if (ShouldThrottleRecovery(connectId)) return result;
                            _lastRecoveryAttempts.AddOrUpdate(connectId, DateTime.UtcNow,
                                (_, _) => DateTime.UtcNow);
                            ExecuteBackgroundTask(() => PerformProtocolResynchronization(connectId),
                                $"ProtocolResync-{connectId}");
                        }
                        else if (FailureClassification.IsProtocolStateMismatch(failure))
                        {
                            Log.Warning(
                                "Protocol state mismatch detected for connection {ConnectId}: {Message}. Forcing fresh protocol establishment",
                                connectId, failure.Message);
                            ExecuteBackgroundTask(() => PerformFreshProtocolEstablishment(connectId),
                                $"FreshProtocol-{connectId}");
                        }

                        return result;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        Log.Information(
                            "[STREAM-TERMINATION] Stream terminated by external cancellation for {ServiceType}",
                            serviceType);
                        throw;
                    }
                    catch (OperationCanceledException) when (flowType == ServiceFlowType.ReceiveStream)
                    {
                        Log.Information(
                            "[STREAM-TERMINATION] Receive stream terminated gracefully for {ServiceType}, treating as successful completion",
                            serviceType);
                        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Unexpected error during {ServiceType} request", serviceType);
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding("Unexpected error during request"));
                    }
                },
                operationName: operationName,
                connectId: connectId,
                maxRetries: 10,
                cancellationToken: operationToken);

            if (!networkResult.IsOk || Volatile.Read(ref _outageState) == 0) return networkResult;
            Log.Information("Request succeeded after outage - clearing outage state");
            ExitOutage();

            return networkResult;
        }
        finally
        {
            if (!shouldAllowDuplicates && _inFlightRequests.TryRemove(requestKey, out CancellationTokenSource? cts))
            {
                cts.Dispose();
            }
        }
    }

    private async Task<Result<Unit, NetworkFailure>> PerformAdvancedRecoveryLogic()
    {
        Log.Information("🔄 RECOVERY: Starting advanced recovery logic");

        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        if (_retryStrategy.HasExhaustedOperations())
        {
            Log.Information(
                "System has exhausted retry operations - skipping automatic reconnection to allow manual retry");
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Restoration failed, awaiting manual retry"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        _connections.TryRemove(connectId, out _);

        Result<byte[], SecureStorageFailure> stateResult =
            await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

        bool restorationSucceeded = false;
        if (stateResult.IsOk)
        {
            try
            {
                byte[] stateBytes = stateResult.Unwrap();
                EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);
                Result<bool, NetworkFailure> restoreResult =
                    await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!);
                restorationSucceeded = restoreResult.IsOk && restoreResult.Unwrap();
                if (restorationSucceeded)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse stored state for connection {ConnectId} during advanced recovery",
                    connectId);
                restorationSucceeded = false;
            }
        }

        if (!restorationSucceeded)
        {
            Log.Warning(
                "Restoration failed for {ConnectId} during advanced recovery - will continue retrying restoration only",
                connectId);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Restoration failed during advanced recovery"));
        }

        if (restorationSucceeded)
        {
            ExitOutage();
            ResetRetryStrategyAfterOutage();

            await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        if (_retryStrategy is SecrecyChannelRetryStrategy retryStrategy)
        {
            if (retryStrategy.HasExhaustedOperations())
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding("Restoration failed, awaiting manual retry"));
            }
        }

        Log.Warning("Restoration failed - will continue retrying restoration only, no new connection establishment");
        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.DataCenterNotResponding("Restoration failed, will retry restoration"));
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

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
        });
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task RetryPendingRequestsAfterRecovery()
    {
        await _retryPendingRequestsGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _pendingRequestManager.RetryAllPendingRequestsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while retrying pending requests after recovery");
        }
        finally
        {
            _retryPendingRequestsGate.Release();
        }
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

        Result<EcliptixSessionState, NetworkFailure> establishResult =
            await EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSessionState secrecyChannelState = establishResult.Unwrap();

        Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
            secrecyChannelState.ToByteString(),
            span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()));

        if (saveResult.IsOk)
        {
            Log.Information("Protocol state saved securely for connection {ConnectId}", connectId);
        }
        else
        {
            Log.Warning("Failed to save protocol state: {Error}", saveResult.UnwrapErr().Message);
        }

        string timestampKey = $"{connectId}_timestamp";
        await _applicationSecureStorageProvider.StoreAsync(timestampKey,
            BitConverter.GetBytes(DateTime.UtcNow.ToBinary()));

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
        });
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    public async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelAsync(
        EcliptixSessionState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);
        }

        string? culture = string.IsNullOrEmpty(applicationInstanceSettings.Culture)
            ? "en-US"
            : applicationInstanceSettings.Culture;
        Log.Information(
            "NetworkProvider: RestoreSecrecyChannel - Setting RpcMetaDataProvider culture to '{Culture}' (original: '{OriginalCulture}')",
            culture, applicationInstanceSettings.Culture);
        _rpcMetaDataProvider.SetAppInfo(Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId), culture);

        RestoreChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse>.New(
                ServiceFlowType.Single,
                RpcServiceType.RestoreSecrecyChannel,
                request);

        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

        Result<RestoreChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse =
            await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                () => _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                    serviceRequest),
                "RestoreSecrecyChannel",
                ecliptixSecrecyChannelState.ConnectId,
                cancellationToken: combinedCts.Token);

        if (restoreAppDeviceSecrecyChannelResponse.IsErr)
            return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());

        RestoreChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        if (response.Status == RestoreChannelResponse.Types.Status.SessionRestored)
        {
            Result<Unit, EcliptixProtocolFailure>
                syncResult = SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
            return syncResult.IsErr
                ? Result<bool, NetworkFailure>.Err(syncResult.UnwrapErr().ToNetworkFailure())
                : Result<bool, NetworkFailure>.Ok(true);
        }

        Log.Information("Session not found on server (status: {Status}), will establish new channel", response.Status);
        return Result<bool, NetworkFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSessionState, NetworkFailure>> EstablishSecrecyChannelAsync(
        uint connectId)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnecting);
        });

        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId,
                Protobuf.Protocol.PubKeyExchangeType.DataCenterEphemeralConnect);

        if (pubKeyExchangeRequest.IsErr)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                pubKeyExchangeRequest.UnwrapErr().ToNetworkFailure());
        }

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
                maxRetries: 15,
                cancellationToken: combinedCts.Token);

        if (establishAppDeviceSecrecyChannelResult.IsErr)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(establishAppDeviceSecrecyChannelResult
                .UnwrapErr());
        }

        PubKeyExchange peerPubKeyExchange = establishAppDeviceSecrecyChannelResult.Unwrap();

        Result<Unit, EcliptixProtocolFailure> completeResult =
            protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange);
        if (completeResult.IsErr)
        {
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                completeResult.UnwrapErr().ToNetworkFailure());
        }

        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection == null)
            return Result<EcliptixSessionState, NetworkFailure>.Err(
                new NetworkFailure(NetworkFailureType.DataCenterNotResponding,
                    "Connection has not been established yet."));

        Result<EcliptixSessionState, EcliptixProtocolFailure> ecliptixSecrecyChannelStateResult =
            idKeys.ToProtoState()
                .AndThen(identityKeysProto => connection.ToProtoState()
                    .Map(ratchetStateProto => new EcliptixSessionState
                    {
                        ConnectId = connectId,
                        IdentityKeys = identityKeysProto,
                        PeerHandshakeMessage = peerPubKeyExchange,
                        RatchetState = ratchetStateProto
                    })
                );

        return ecliptixSecrecyChannelStateResult.ToNetworkFailure();
    }

    public async Task<Result<uint, NetworkFailure>> EnsureProtocolForTypeAsync(
        PubKeyExchangeType exchangeType)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application not initialized"));
        }

        ApplicationInstanceSettings appSettings = _applicationInstanceSettings.Value!;
        uint connectId = ComputeUniqueConnectId(appSettings, exchangeType);

        if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? existingConnection))
        {
            Log.Information(
                "[PROTOCOL] Found existing protocol for connectId {ConnectId}, checking config compatibility...",
                connectId);

            RatchetConfig expectedConfig = GetRatchetConfigForExchangeType(exchangeType);

            Log.Information("[PROTOCOL] Recreating protocol for type {Type} to ensure correct configuration",
                exchangeType);
            _connections.TryRemove(connectId, out _);
            existingConnection?.Dispose();
        }

        Log.Information("[PROTOCOL] Creating new protocol for type {Type}, connectId {ConnectId}",
            exchangeType, connectId);

        InitiateEcliptixProtocolSystemForType(connectId, exchangeType);

        Result<Option<EcliptixSessionState>, NetworkFailure> establishOptionResult =
            await EstablishSecrecyChannelForTypeAsync(connectId, exchangeType);

        if (establishOptionResult.IsErr)
        {
            _connections.TryRemove(connectId, out _);
            return Result<uint, NetworkFailure>.Err(establishOptionResult.UnwrapErr());
        }

        Option<EcliptixSessionState> optionState = establishOptionResult.Unwrap();

        if (!optionState.HasValue) return Result<uint, NetworkFailure>.Ok(connectId);

        Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
            optionState.Value.ToByteString(),
            span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()));

        if (saveResult.IsErr)
        {
            Log.Warning("Failed to save protocol state for type {Type}: {Error}",
                exchangeType, saveResult.UnwrapErr());
        }

        return Result<uint, NetworkFailure>.Ok(connectId);
    }

    public async Task RemoveProtocolForTypeAsync(PubKeyExchangeType exchangeType)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application not initialized"));
            return;
        }

        ApplicationInstanceSettings appSettings = _applicationInstanceSettings.Value!;
        uint connectId = ComputeUniqueConnectId(appSettings, exchangeType);

        if (!_connections.ContainsKey(connectId) && !_activeStreams.ContainsKey(connectId))
        {
            return;
        }

        CancelOperationsForConnection(connectId);

        if (_activeStreams.TryRemove(connectId, out CancellationTokenSource? streamCts))
        {
            streamCts.Cancel();
            streamCts.Dispose();
        }

        if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            protocolSystem.Dispose();

            Result<Unit, SecureStorageFailure> deleteResult =
                await _secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

            if (deleteResult.IsErr)
            {
                Log.Warning("Failed to delete protocol state for type {Type}: {Error}",
                    exchangeType, deleteResult.UnwrapErr());
            }
        }
        else
        {
        }

        Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private void CancelOperationsForConnection(uint connectId)
    {
        string connectIdPrefix = $"{connectId}_";
        List<string> keysToCancel = [];
        keysToCancel.AddRange(_inFlightRequests.Keys.Where(key => key.StartsWith(connectIdPrefix)));

        foreach (string key in keysToCancel)
        {
            if (!_inFlightRequests.TryRemove(key, out CancellationTokenSource? operationCts)) continue;
            try
            {
                operationCts.Cancel();
                operationCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    public async Task<Result<Unit, NetworkFailure>> CleanupStreamProtocolAsync(uint connectId)
    {
        CancelOperationsForConnection(connectId);

        if (_activeStreams.TryRemove(connectId, out CancellationTokenSource? streamCts))
        {
            await streamCts.CancelAsync();
            streamCts.Dispose();
        }

        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? protocolSystem))
            return Result<Unit, NetworkFailure>.Ok(Unit.Value);

        protocolSystem.Dispose();

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private static RatchetConfig GetRatchetConfigForExchangeType(PubKeyExchangeType exchangeType)
    {
        RatchetConfig config = exchangeType switch
        {
            PubKeyExchangeType.ServerStreaming => new RatchetConfig
            {
                DhRatchetEveryNMessages = 20,
                MaxChainAge = TimeSpan.FromMinutes(5),
                MaxMessagesWithoutRatchet = 100
            },
            _ => RatchetConfig.Default
        };

        Log.Information("[RATCHET-CONFIG] Created config for {ExchangeType}: DH every {Messages} messages",
            exchangeType, config.DhRatchetEveryNMessages);
        return config;
    }

    private static PubKeyExchangeType DetermineExchangeTypeFromConnectId(
        ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        PubKeyExchangeType[] knownTypes =
        [
            PubKeyExchangeType.DataCenterEphemeralConnect,
            PubKeyExchangeType.ServerStreaming
        ];

        foreach (PubKeyExchangeType exchangeType in knownTypes)
        {
            uint testConnectId = ComputeUniqueConnectId(applicationInstanceSettings, exchangeType);
            if (testConnectId != connectId) continue;
            Log.Information("NetworkProvider: Determined exchange type {ExchangeType} for connectId {ConnectId}",
                exchangeType, connectId);
            return exchangeType;
        }

        Log.Warning("NetworkProvider: Could not determine exchange type for connectId {ConnectId}, using default",
            connectId);
        return PubKeyExchangeType.DataCenterEphemeralConnect;
    }

    private void InitiateEcliptixProtocolSystemForType(uint connectId,
        PubKeyExchangeType exchangeType)
    {
        Log.Information("[PROTOCOL] Creating protocol system for type {Type}", exchangeType);

        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(DefaultOneTimeKeyCount).Unwrap();

        RatchetConfig config = GetRatchetConfigForExchangeType(exchangeType);
        EcliptixProtocolSystem protocolSystem = new(identityKeys, config);
        protocolSystem.SetEventHandler(this);

        bool addSuccess = _connections.TryAdd(connectId, protocolSystem);
        Log.Information(
            "[PROTOCOL-TRACKING] Added protocol to connections - ConnectId: {ConnectId}, Success: {Success}, Config.DhEvery: {DhEvery}",
            connectId, addSuccess, config.DhRatchetEveryNMessages);

        ConnectionHealth initialHealth = new()
        {
            ConnectId = connectId,
            Status = ConnectionHealthStatus.Healthy
        };
        _connectionStateManager.RegisterConnection(connectId, initialHealth);
    }

    private async Task<Result<Option<EcliptixSessionState>, NetworkFailure>> EstablishSecrecyChannelForTypeAsync(
        uint connectId,
        PubKeyExchangeType exchangeType)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Protocol system not found"));
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnecting);
        });

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, exchangeType);

        if (pubKeyExchangeRequest.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                pubKeyExchangeRequest.UnwrapErr().ToNetworkFailure());
        }

        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> action =
            SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange>.New(
                ServiceFlowType.Single,
                RpcServiceType.EstablishSecrecyChannel,
                pubKeyExchangeRequest.Unwrap(),
                exchangeType);

        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);

        Result<PubKeyExchange, NetworkFailure> establishAppDeviceSecrecyChannelResult =
            await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                () => _rpcServiceManager.EstablishAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents, action),
                "EstablishSecrecyChannel",
                connectId,
                maxRetries: 15,
                cancellationToken: combinedCts.Token);

        if (establishAppDeviceSecrecyChannelResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(establishAppDeviceSecrecyChannelResult
                .UnwrapErr());
        }

        PubKeyExchange peerPubKeyExchange = establishAppDeviceSecrecyChannelResult.Unwrap();

        Result<Unit, EcliptixProtocolFailure> completeResult =
            protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange);
        if (completeResult.IsErr)
        {
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                completeResult.UnwrapErr().ToNetworkFailure());
        }

        EcliptixSystemIdentityKeys idKeys = protocolSystem.GetIdentityKeys();
        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
        if (connection == null)
            return Result<Option<EcliptixSessionState>, NetworkFailure>.Err(
                new NetworkFailure(NetworkFailureType.DataCenterNotResponding,
                    "Connection has not been established yet."));

        if (exchangeType == PubKeyExchangeType.DataCenterEphemeralConnect)
        {
            Result<EcliptixSessionState, EcliptixProtocolFailure> ecliptixSecrecyChannelStateResult =
                idKeys.ToProtoState()
                    .AndThen(identityKeysProto => connection.ToProtoState()
                        .Map(ratchetStateProto => new EcliptixSessionState
                        {
                            ConnectId = connectId,
                            IdentityKeys = identityKeysProto,
                            PeerHandshakeMessage = peerPubKeyExchange,
                            RatchetState = ratchetStateProto
                        })
                    );

            return ecliptixSecrecyChannelStateResult.ToNetworkFailure().Map(Option<EcliptixSessionState>.Some);
        }

        return Result<Option<EcliptixSessionState>, NetworkFailure>.Ok(Option<EcliptixSessionState>.None);
    }

    private Result<Unit, EcliptixProtocolFailure> SyncSecrecyChannel(
        EcliptixSessionState currentState,
        RestoreChannelResponse peerSecrecyChannelState)
    {
        Result<EcliptixProtocolSystem, EcliptixProtocolFailure> systemResult = RecreateSystemFromState(currentState);
        if (systemResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(systemResult.UnwrapErr());
        }

        EcliptixProtocolSystem system = systemResult.Unwrap();

        system.SetEventHandler(this);

        EcliptixProtocolConnection? connection = system.GetConnection();
        if (connection == null)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not established"));

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

    private Result<EcliptixProtocolSystem, EcliptixProtocolFailure> RecreateSystemFromState(
        EcliptixSessionState state)
    {
        Result<EcliptixSystemIdentityKeys, EcliptixProtocolFailure> idKeysResult =
            EcliptixSystemIdentityKeys.FromProtoState(state.IdentityKeys);
        if (idKeysResult.IsErr)
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(idKeysResult.UnwrapErr());

        PubKeyExchangeType exchangeType = _applicationInstanceSettings.HasValue
            ? DetermineExchangeTypeFromConnectId(_applicationInstanceSettings.Value!, state.ConnectId)
            : PubKeyExchangeType.DataCenterEphemeralConnect;

        RatchetConfig config = GetRatchetConfigForExchangeType(exchangeType);

        Result<EcliptixProtocolConnection, EcliptixProtocolFailure> connResult =
            EcliptixProtocolConnection.FromProtoState(state.ConnectId, state.RatchetState, config, exchangeType);

        if (connResult.IsErr)
        {
            idKeysResult.Unwrap().Dispose();
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(connResult.UnwrapErr());
        }

        Log.Information(
            "[RESTORE] Recreated protocol connection for connectId {ConnectId} with exchange type {ExchangeType} - DH every {Messages} messages",
            state.ConnectId, exchangeType, config.DhRatchetEveryNMessages);

        return EcliptixProtocolSystem.CreateFrom(idKeysResult.Unwrap(), connResult.Unwrap(), config);
    }

    private uint GenerateLogicalOperationId(uint connectId, RpcServiceType serviceType, byte[] plainBuffer)
    {
        string logicalSemantic = serviceType.ToString() switch
        {
            "OpaqueSignInInitRequest" or "OpaqueSignInFinalizeRequest" =>
                $"auth:signin:{connectId}",
            "OpaqueSignUpInitRequest" or "OpaqueSignUpFinalizeRequest" =>
                $"auth:signup:{connectId}",

            "InitiateVerification" =>
                $"stream:{serviceType}:{connectId}:{DateTime.UtcNow.Ticks}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(plainBuffer))}",

            _ =>
                $"data:{serviceType}:{connectId}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(plainBuffer))}"
        };

        byte[] semanticBytes = System.Text.Encoding.UTF8.GetBytes(logicalSemantic);
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(semanticBytes);

        uint rawId = BitConverter.ToUInt32(hashBytes, 0);
        uint finalId = Math.Max(rawId % (uint.MaxValue - 10), 10);

        if (serviceType == RpcServiceType.InitiateVerification)
        {
            Log.Information(
                "[OPERATION-ID] Generated unique ID for InitiateVerification: {OperationId} (semantic: {LogicalSemantic})",
                finalId, logicalSemantic);
        }

        return finalId;
    }

    private static Result<ServiceRequest, NetworkFailure> BuildRequestWithId(
        EcliptixProtocolSystem protocolSystem,
        uint logicalOperationId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType)
    {
        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);

        if (outboundPayload.IsErr)
        {
            return Result<ServiceRequest, NetworkFailure>.Err(
                outboundPayload.UnwrapErr().ToNetworkFailure());
        }

        CipherPayload cipherPayload = outboundPayload.Unwrap();

        return Result<ServiceRequest, NetworkFailure>.Ok(
            ServiceRequest.New(logicalOperationId, flowType, serviceType, cipherPayload, []));
    }

    private static Result<ServiceRequest, NetworkFailure> BuildRequest(
        EcliptixProtocolSystem protocolSystem,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType)
    {
        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);

        if (outboundPayload.IsErr)
        {
            return Result<ServiceRequest, NetworkFailure>.Err(
                outboundPayload.UnwrapErr().ToNetworkFailure());
        }

        CipherPayload cipherPayload = outboundPayload.Unwrap();

        return Result<ServiceRequest, NetworkFailure>.Ok(
            ServiceRequest.New(flowType, serviceType, cipherPayload, []));
    }

    private async Task<Result<Unit, NetworkFailure>> SendUnaryRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.SingleCall singleCall)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Expected SingleCall flow but received {flow.GetType().Name}"));
        }

        Result<CipherPayload, NetworkFailure> callResult = await singleCall.Result;
        if (callResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(callResult.UnwrapErr());
        }

        CipherPayload inboundPayload = callResult.Unwrap();

        Result<byte[], EcliptixProtocolFailure> decryptedData =
            protocolSystem.ProcessInboundMessage(inboundPayload);
        if (decryptedData.IsErr)
        {
            Log.Warning("Decryption failed: {Error}", decryptedData.UnwrapErr().Message);
            return Result<Unit, NetworkFailure>.Err(decryptedData.UnwrapErr().ToNetworkFailure());
        }

        await onCompleted(decryptedData.Unwrap());
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> ReExecuteServiceFromPlaintextAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        uint originalReqId,
        CancellationToken token)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        Result<ServiceRequest, NetworkFailure> requestResult =
            BuildRequest(protocolSystem, serviceType, plainBuffer, flowType);

        if (requestResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(requestResult.UnwrapErr());
        }

        ServiceRequest request = requestResult.Unwrap();

        Log.Information(
            "Re-executing request with fresh encryption - original ReqId {OriginalReqId}, new ReqId {NewReqId}",
            originalReqId, request.ReqId);

        Result<Unit, NetworkFailure> result = request.ActionType switch
        {
            ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem, request, onCompleted,
                token),
            ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(protocolSystem, request,
                onCompleted, token),
            _ => Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Unsupported flow type: {request.ActionType}"))
        };

        if (result.IsOk)
        {
        }

        return result;
    }

    private async Task<Result<Unit, NetworkFailure>> SendReceiveStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        uint connectId = _connections.FirstOrDefault(kvp => kvp.Value == protocolSystem).Key;
        if (connectId == 0)
        {
            return await ProcessStreamDirectly(protocolSystem, request, onStreamItem, token);
        }

        using CancellationTokenSource streamCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _activeStreams.TryAdd(connectId, streamCts);

        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.InboundStream inboundStream)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Expected InboundStream flow but received {flow.GetType().Name}"));
        }

        try
        {
            await foreach (Result<CipherPayload, NetworkFailure> streamItem in
                           inboundStream.Stream.WithCancellation(streamCts.Token))
            {
                if (streamItem.IsErr)
                {
                    Result<Unit, NetworkFailure> errResult = Result<Unit, NetworkFailure>.Err(
                        streamItem.UnwrapErr());

                    return errResult;
                }

                CipherPayload streamPayload = streamItem.Unwrap();
                Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                    protocolSystem.ProcessInboundMessage(streamPayload);
                if (streamDecryptedData.IsErr)
                {
                    continue;
                }

                await onStreamItem(streamDecryptedData.Unwrap());
            }
        }
        catch (OperationCanceledException) when (streamCts.Token.IsCancellationRequested)
        {
            Log.Information(
                "[STREAM-CANCEL] Stream cancelled by server or cleanup - terminating gracefully. ConnectId: {ConnectId}",
                connectId);
        }
        catch (OperationCanceledException)
        {
            Log.Information("[STREAM-CANCEL] Stream cancelled by external token - ConnectId: {ConnectId}", connectId);
            throw;
        }
        finally
        {
            if (_activeStreams.TryRemove(connectId, out _))
            {
            }
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> ProcessStreamDirectly(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            Log.Warning("InvokeServiceRequestAsync failed: {Error}", invokeResult.UnwrapErr().Message);
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.InboundStream inboundStream)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Expected InboundStream flow but received {flow.GetType().Name}"));
        }

        await foreach (Result<CipherPayload, NetworkFailure> streamItem in
                       inboundStream.Stream.WithCancellation(token))
        {
            if (streamItem.IsErr)
            {
                Log.Warning("Stream item error: {Error}", streamItem.UnwrapErr().Message);
                continue;
            }

            CipherPayload streamPayload = streamItem.Unwrap();
            Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                protocolSystem.ProcessInboundMessage(streamPayload);
            if (streamDecryptedData.IsErr)
            {
                Log.Warning("Stream decryption failed: {Error}", streamDecryptedData.UnwrapErr().Message);
                continue;
            }

            await onStreamItem(streamDecryptedData.Unwrap());
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }


    private async Task<Result<Unit, NetworkFailure>> SendSendStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            Log.Warning("InvokeServiceRequestAsync failed: {Error}", invokeResult.UnwrapErr().Message);
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.OutboundSink)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Expected OutboundSink flow but received {flow.GetType().Name}"));
        }

        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Client streaming is not yet implemented"));
    }

    private async Task<Result<Unit, NetworkFailure>> SendBidirectionalStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            Log.Warning("InvokeServiceRequestAsync failed: {Error}", invokeResult.UnwrapErr().Message);
            return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());
        }

        RpcFlow flow = invokeResult.Unwrap();
        if (flow is not RpcFlow.BidirectionalStream)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(
                    $"Expected BidirectionalStream flow but received {flow.GetType().Name}"));
        }

        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Bidirectional streaming is not yet implemented"));
    }

    private async Task WaitForOutageRecoveryAsync(CancellationToken token, bool waitForRecovery = true)
    {
        if (Volatile.Read(ref _outageState) == 0) return;
        if (!waitForRecovery) return;

        Task waitTask;
        lock (_outageLock)
        {
            waitTask = _outageRecoveredTcs.Task;
        }

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdownCts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        try
        {
            await waitTask.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_shutdownCts.Token.IsCancellationRequested)
        {
            throw new ObjectDisposedException(nameof(NetworkProvider), "Provider is shutting down");
        }
    }

    private void EnterOutage(string reason, uint connectId)
    {
        if (Interlocked.Exchange(ref _outageState, 1) != 0) return;

        lock (_outageLock)
        {
            _outageRecoveredTcs = CreateOutageTcs();
        }


        _currentSystemState = SystemState.Recovering;

        Log.Warning("Server appears unavailable/shutdown (ConnectId: {ConnectId}). Entering recovery mode: {Reason}",
            connectId, reason);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ServerShutdown);
            _ = _systemEvents.NotifySystemStateAsync(SystemState.Recovering);
        });

        CancelActiveRecoveryOperations();
        CancelActiveRequestsDuringRecovery("Server shutdown detected");

        ExecuteBackgroundTask(ServerShutdownRecoveryLoop, $"ServerShutdownRecovery-{connectId}");
    }

    private void ExitOutage()
    {
        if (Interlocked.Exchange(ref _outageState, 0) == 0) return;

        lock (_outageLock)
        {
            if (!_outageRecoveredTcs.Task.IsCompleted)
                _outageRecoveredTcs.TrySetResult(true);
        }


        _currentSystemState = SystemState.Running;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ConnectionRestored);
            _ = _systemEvents.NotifySystemStateAsync(SystemState.Running);
        });

        Log.Information("Recovery completed. Secrecy channel is healthy; resuming all requests");
    }

    private async Task ServerShutdownRecoveryLoop()
    {
        try
        {
            CancellationToken token = GetConnectionRecoveryToken();
            int attempt = 0;

            while (!_disposed && !token.IsCancellationRequested)
            {
                attempt++;

                if (_retryStrategy.HasExhaustedOperations())
                {
                    Log.Information(
                        "🛑 RECOVERY STOPPED: Retry strategy has exhausted operations - waiting for manual retry");

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.RetriesExhausted);
                    });

                    return;
                }

                Result<Unit, NetworkFailure> result = await PerformAdvancedRecoveryLogic();

                if (result.IsOk)
                {
                    ExitOutage();
                    ResetRetryStrategyAfterOutage();
                    return;
                }

                TimeSpan delay = ComputeOutageBackoff(attempt);
                Log.Warning(
                    "Server still unavailable; retrying secrecy channel recovery in {Delay} (attempt {Attempt})",
                    delay, attempt);

                try
                {
                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in server shutdown recovery loop");


            if (_retryStrategy.HasExhaustedOperations())
            {
                try
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.RetriesExhausted);
                    });
                }
                catch (Exception uiEx)
                {
                    Log.Error(uiEx, "Failed to dispatch retry exhausted status to UI thread");
                }
            }
        }
    }

    private static TimeSpan ComputeOutageBackoff(int attempt)
    {
        int cappedPow = Math.Min(attempt - 1, 6);
        int baseMs = Math.Min(500 * (int)Math.Pow(2, Math.Max(0, cappedPow)), 8000);
        int jitter = Random.Shared.Next(0, baseMs / 2 + 1);
        return TimeSpan.FromMilliseconds(baseMs / 2 + jitter);
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
                Log.Information("Cancelling active recovery operations");
                _connectionRecoveryCts.Cancel();
                _connectionRecoveryCts.Dispose();
            }

            _connectionRecoveryCts = new CancellationTokenSource();
        }
    }

    private void CancelActiveRequestsDuringRecovery(string reason)
    {
        if (_inFlightRequests.IsEmpty) return;

        Log.Information("Cancelling {Count} active requests during recovery: {Reason}",
            _inFlightRequests.Count, reason);

        foreach (KeyValuePair<string, CancellationTokenSource> kv in _inFlightRequests.ToArray())
        {
            if (!_inFlightRequests.TryRemove(kv.Key, out CancellationTokenSource? cts)) continue;
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private async Task<T> WithChannelGate<T>(uint connectId, Func<Task<T>> action)
    {
        SemaphoreSlim gate = _channelGates.GetOrAdd(connectId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<T> WithRequestExecutionGate<T>(string operationKey, Func<Task<T>> action)
    {
        SemaphoreSlim gate = _logicalOperationGates.GetOrAdd(operationKey, _ => new SemaphoreSlim(1, 1));


        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(30));

        try
        {
            await gate.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Timeout waiting for execution gate: {operationKey}");
        }

        try
        {
            T result = await action().ConfigureAwait(false);
            Log.Debug("Operation completed successfully: {OperationKey}", operationKey);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Exception in gate-protected operation: {OperationKey}", operationKey);
            throw;
        }
        finally
        {
            try
            {
                gate.Release();

                if (_logicalOperationGates.TryRemove(operationKey, out SemaphoreSlim? removedGate))
                {
                    removedGate?.Dispose();
                }
            }
            catch (Exception cleanupEx)
            {
                Log.Warning(cleanupEx, "Failed to cleanup gate for operation: {OperationKey}", operationKey);
                _logicalOperationGates.TryRemove(operationKey, out _);
            }
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

            Log.Information("Initiating recovery with cancellation for connection {ConnectId}", connectId);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ConnectionRecovering);
            });

            if (!_applicationInstanceSettings.HasValue)
            {
                Log.Warning("Cannot recover connection {ConnectId} - application settings not available", connectId);
                return;
            }

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.RestoreSecrecyChannel);
            });

            Result<byte[], SecureStorageFailure> stateResult =
                await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

            bool restorationSuccessful;
            if (stateResult.IsOk)
            {
                try
                {
                    EcliptixSessionState state =
                        EcliptixSessionState.Parser.ParseFrom(stateResult.Unwrap());
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
                Result<EcliptixSessionState, NetworkFailure> newResult =
                    await EstablishSecrecyChannelAsync(connectId);
                restorationSuccessful = newResult.IsOk;
            }

            if (restorationSuccessful)
            {
                Log.Information("Recovery completed for connection {ConnectId}", connectId);
                ExitOutage();
                ResetRetryStrategyAfterOutage();
            }
            else
            {
                Log.Warning("Recovery failed for connection {ConnectId}, falling back to reconnection",
                    connectId);
                await PerformReconnectionLogic();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during connection recovery for {ConnectId}", connectId);
        }
    }

    private static bool ShouldAllowDuplicateRequests(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.InitiateVerification => true,
            RpcServiceType.ValidatePhoneNumber => true,
            _ => false
        };
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
                        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
                        if (connection == null)
                            return;

                        if (connection.ExchangeType == PubKeyExchangeType.ServerStreaming)
                        {
                            Log.Information(
                                "DH Ratchet performed for SERVER_STREAMING - ConnectId: {ConnectId}, Type: {Type}, Index: {Index}. Skipping persistence (memory-only)",
                                connectId, isSending ? "Sending" : "Receiving", newIndex);
                            return;
                        }

                        Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
                        Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

                        if (idKeysStateResult.IsOk && ratchetStateResult.IsOk)
                        {
                            EcliptixSessionState state = new()
                            {
                                ConnectId = connectId,
                                IdentityKeys = idKeysStateResult.Unwrap(),
                                RatchetState = ratchetStateResult.Unwrap()
                            };

                            Result<Unit, SecureStorageFailure> saveResult =
                                await SecureByteStringInterop.WithByteStringAsSpan(state.ToByteString(),
                                    span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(),
                                        connectId.ToString()));

                            if (saveResult.IsOk)
                            {
                                Log.Information(
                                    "Protocol state saved successfully after {ChainType} chain rotation - ConnectId: {ConnectId}, Index: {Index}",
                                    isSending ? "sending" : "receiving", connectId, newIndex);
                            }
                            else
                            {
                                Log.Warning("Failed to save protocol state after DH ratchet: {Error}",
                                    saveResult.UnwrapErr().Message);
                            }

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
                        EcliptixProtocolConnection? connection = protocolSystem.GetConnection();
                        if (connection == null)
                            return;

                        if (connection.ExchangeType == PubKeyExchangeType.ServerStreaming)
                        {
                            Log.Information(
                                "Chain synchronized for SERVER_STREAMING - ConnectId: {ConnectId}, Local: {Local}, Remote: {Remote}. Skipping persistence (memory-only)",
                                connectId, localLength, remoteLength);
                            return;
                        }

                        Result<IdentityKeysState, EcliptixProtocolFailure> idKeysStateResult = idKeys.ToProtoState();
                        Result<RatchetState, EcliptixProtocolFailure> ratchetStateResult = connection.ToProtoState();

                        if (idKeysStateResult.IsOk && ratchetStateResult.IsOk)
                        {
                            EcliptixSessionState state = new()
                            {
                                ConnectId = connectId,
                                IdentityKeys = idKeysStateResult.Unwrap(),
                                RatchetState = ratchetStateResult.Unwrap()
                            };

                            Result<Unit, SecureStorageFailure> saveResult =
                                await SecureByteStringInterop.WithByteStringAsSpan(state.ToByteString(),
                                    span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(),
                                        connectId.ToString()));

                            if (saveResult.IsOk)
                            {
                                Log.Information(
                                    "Protocol state saved successfully after chain synchronization - ConnectId: {ConnectId}",
                                    connectId);
                            }
                            else
                            {
                                Log.Warning("Failed to save protocol state after chain sync: {Error}",
                                    saveResult.UnwrapErr().Message);
                            }

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
        Log.Debug(
            "Message processed - ConnectId: {ConnectId}, Index: {Index}, SkippedKeys: {SkippedKeys}",
            connectId, messageIndex, hasSkippedKeys);
    }

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
                _shutdownCts.Cancel();

                _connectionStateManager.Dispose();

                foreach (KeyValuePair<string, CancellationTokenSource> kv in _inFlightRequests.ToArray())
                {
                    if (!_inFlightRequests.TryRemove(kv.Key, out CancellationTokenSource? cts)) continue;
                    try
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                foreach (KeyValuePair<uint, CancellationTokenSource> kv in _activeStreams.ToArray())
                {
                    if (!_activeStreams.TryRemove(kv.Key, out CancellationTokenSource? streamCts)) continue;
                    try
                    {
                        Log.Information("[DISPOSE] Cancelling active stream for connectId {ConnectId}", kv.Key);
                        streamCts.Cancel();
                        streamCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }

                _lastRecoveryAttempts.Clear();
                _lastRequestAttempts.Clear();

                lock (_outageLock)
                {
                    _outageRecoveredTcs.TrySetException(new OperationCanceledException("Provider shutting down"));
                }

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

                foreach (SemaphoreSlim gate in _channelGates.Values)
                {
                    gate.Dispose();
                }

                _channelGates.Clear();

                _retryPendingRequestsGate.Dispose();

                foreach (SemaphoreSlim gate in _logicalOperationGates.Values)
                {
                    gate.Dispose();
                }

                _logicalOperationGates.Clear();

                _shutdownCts.Dispose();

                Log.Information("NetworkProvider disposed safely");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during NetworkProvider disposal");
            }
        }
    }

    private async Task<Result<Unit, NetworkFailure>> PerformProtocolResynchronization(uint connectId)
    {
        try
        {
            if (!_applicationInstanceSettings.HasValue)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Application instance settings not available for resync"));
            }

            if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? oldSystem))
            {
                try
                {
                    oldSystem.Dispose();
                    Log.Debug("Disposed old protocol system for connection {ConnectId}", connectId);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error disposing old protocol system for connection {ConnectId}", connectId);
                }
            }

            await _secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());
            Log.Debug("Cleared stored protocol state for resynchronization");

            InitiateEcliptixProtocolSystem(_applicationInstanceSettings.Value!, connectId);

            Result<EcliptixSessionState, NetworkFailure> establishResult =
                await EstablishSecrecyChannelAsync(connectId);

            if (establishResult.IsErr)
            {
                Log.Error("Failed to re-establish secrecy channel during resynchronization: {Error}",
                    establishResult.UnwrapErr());
                return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
            }

            EcliptixSessionState newState = establishResult.Unwrap();
            Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                newState.ToByteString(),
                span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()));

            if (saveResult.IsOk)
            {
            }
            else
            {
                Log.Warning("Protocol resynchronized but failed to save state: {Error}", saveResult.UnwrapErr());
            }

            await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during protocol resynchronization for connection {ConnectId}", connectId);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Resynchronization failed: {ex.Message}"));
        }
    }

    public async Task<Result<Unit, NetworkFailure>> ForceFreshConnectionAsync()
    {
        _retryStrategy.ClearExhaustedOperations();


        Result<Unit, NetworkFailure> immediateResult = await PerformImmediateRecoveryLogic();

        if (immediateResult.IsOk)
        {
            return immediateResult;
        }


        Result<Unit, NetworkFailure> result = await PerformAdvancedRecoveryWithManualRetryAsync();

        return result;
    }

    private async Task<Result<Unit, NetworkFailure>> PerformAdvancedRecoveryWithManualRetryAsync()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);
        _connections.TryRemove(connectId, out _);

        Result<byte[], SecureStorageFailure> stateResult =
            await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

        bool restorationSucceeded = false;
        if (stateResult.IsOk)
        {
            try
            {
                byte[] stateBytes = stateResult.Unwrap();
                EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);

                Result<bool, NetworkFailure> restoreResult =
                    await RestoreSecrecyChannelForManualRetryAsync(state, _applicationInstanceSettings.Value!);
                restorationSucceeded = restoreResult.IsOk && restoreResult.Unwrap();
                if (restorationSucceeded)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        _ = _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse stored state for connection {ConnectId} during manual retry recovery",
                    connectId);
                restorationSucceeded = false;
            }
        }

        if (!restorationSucceeded)
        {
            Log.Warning("Manual retry restoration failed for {ConnectId} - will continue retrying restoration only",
                connectId);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Manual retry restoration failed"));
        }

        if (restorationSucceeded)
        {
            ExitOutage();
            ResetRetryStrategyAfterOutage();
            await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);
            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        Log.Warning("Manual retry failed to restore connection for {ConnectId}", connectId);
        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.DataCenterNotResponding("Manual retry failed to restore connection"));
    }

    private async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelForManualRetryAsync(
        EcliptixSessionState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        _rpcMetaDataProvider.SetAppInfo(Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId), applicationInstanceSettings.Culture);

        RestoreChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse>.New(
                ServiceFlowType.Single,
                RpcServiceType.RestoreSecrecyChannel,
                request);

        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(recoveryToken);


        Result<RestoreChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse =
            await _retryStrategy.ExecuteManualRetryOperationAsync(
                () => _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                    serviceRequest),
                "RestoreSecrecyChannel",
                ecliptixSecrecyChannelState.ConnectId,
                cancellationToken: combinedCts.Token);

        if (restoreAppDeviceSecrecyChannelResponse.IsErr)
            return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());

        RestoreChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        if (response.Status == RestoreChannelResponse.Types.Status.SessionRestored)
        {
            Result<Unit, EcliptixProtocolFailure>
                syncResult = SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
            return syncResult.IsErr
                ? Result<bool, NetworkFailure>.Err(syncResult.UnwrapErr().ToNetworkFailure())
                : Result<bool, NetworkFailure>.Ok(true);
        }

        Log.Information("Session not found on server (status: {Status}), will establish new channel", response.Status);
        return Result<bool, NetworkFailure>.Ok(false);
    }

    private async Task<Result<Unit, NetworkFailure>> PerformImmediateRecoveryLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!,
            PubKeyExchangeType.DataCenterEphemeralConnect);
        _connections.TryRemove(connectId, out _);

        Result<byte[], SecureStorageFailure> stateResult =
            await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

        if (stateResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("No stored state for immediate recovery"));
        }

        try
        {
            byte[] stateBytes = stateResult.Unwrap();
            EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);


            Result<bool, NetworkFailure> restoreResult =
                await RestoreSecrecyChannelDirectAsync(state, _applicationInstanceSettings.Value!);

            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                ExitOutage();
                ResetRetryStrategyAfterOutage();
                await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);

                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Session not found on server"));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "🔄 IMMEDIATE RECOVERY: Failed to parse stored state for connection {ConnectId}",
                connectId);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Failed to parse stored state: {ex.Message}"));
        }
    }

    private async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannelDirectAsync(
        EcliptixSessionState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        _rpcMetaDataProvider.SetAppInfo(Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId), applicationInstanceSettings.Culture);

        RestoreChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse>.New(
                ServiceFlowType.Single,
                RpcServiceType.RestoreSecrecyChannel,
                request);

        try
        {
            Result<RestoreChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse =
                await _rpcServiceManager.RestoreAppDeviceSecrecyChannelAsync(_networkEvents, _systemEvents,
                    serviceRequest);

            if (restoreAppDeviceSecrecyChannelResponse.IsErr)
            {
                Log.Information("🔄 DIRECT RESTORE: RestoreSecrecyChannel RPC failed: {Error}",
                    restoreAppDeviceSecrecyChannelResponse.UnwrapErr().Message);
                return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());
            }

            RestoreChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

            if (response.Status == RestoreChannelResponse.Types.Status.SessionRestored)
            {
                Result<Unit, EcliptixProtocolFailure> syncResult =
                    SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
                if (syncResult.IsErr)
                {
                    return Result<bool, NetworkFailure>.Err(syncResult.UnwrapErr().ToNetworkFailure());
                }

                return Result<bool, NetworkFailure>.Ok(true);
            }

            return Result<bool, NetworkFailure>.Ok(false);
        }
        catch (Exception ex)
        {
            return Result<bool, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }

    private async Task<Result<Unit, NetworkFailure>> PerformFreshProtocolEstablishment(uint connectId)
    {
        try
        {
            if (!_applicationInstanceSettings.HasValue)
            {
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Application instance settings not available"));
            }

            if (_connections.TryRemove(connectId, out EcliptixProtocolSystem? oldSystem))
            {
                try
                {
                    oldSystem.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error disposing old protocol system");
                }
            }

            await _secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

            InitiateEcliptixProtocolSystem(_applicationInstanceSettings.Value!, connectId);

            Result<EcliptixSessionState, NetworkFailure> establishResult =
                await EstablishSecrecyChannelAsync(connectId);

            if (establishResult.IsErr)
            {
                Log.Error("Failed fresh protocol establishment: {Error}", establishResult.UnwrapErr());
                return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
            }

            EcliptixSessionState freshState = establishResult.Unwrap();
            Result<Unit, SecureStorageFailure> saveResult = await SecureByteStringInterop.WithByteStringAsSpan(
                freshState.ToByteString(),
                span => _secureProtocolStateStorage.SaveStateAsync(span.ToArray(), connectId.ToString()));

            if (saveResult.IsOk)
            {
            }
            else
            {
                Log.Warning("Fresh protocol established but failed to save state: {Error}", saveResult.UnwrapErr());
            }

            await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Exception during fresh protocol establishment for connection {ConnectId}", connectId);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Fresh establishment failed: {ex.Message}"));
        }
    }

    private void ResetRetryStrategyAfterOutage()
    {
        _retryStrategy.ResetConnectionState();

        foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in _connections)
        {
            _retryStrategy.MarkConnectionHealthy(connection.Key);
        }

        _lastRecoveryAttempts.Clear();
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? _))
            return false;

        ConnectionHealth? health = _connectionStateManager.GetConnectionHealth(connectId);
        return health?.Status == ConnectionHealthStatus.Healthy;
    }

    public async Task<Result<bool, NetworkFailure>> TryRestoreConnectionAsync(uint connectId)
    {
        return await WithChannelGate(connectId, async () =>
        {
            try
            {
                Result<byte[], SecureStorageFailure> stateResult =
                    await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString()).ConfigureAwait(false);
                if (stateResult.IsErr)
                {
                    Log.Debug("No saved state found for connection {ConnectId}", connectId);
                    return Result<bool, NetworkFailure>.Ok(false);
                }

                byte[] stateBytes = stateResult.Unwrap();
                EcliptixSessionState state = EcliptixSessionState.Parser.ParseFrom(stateBytes);
                Result<bool, NetworkFailure> restoreResult =
                    await RestoreSecrecyChannelAsync(state, _applicationInstanceSettings.Value!).ConfigureAwait(false);

                return restoreResult;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to restore connection {ConnectId}", connectId);
                return Result<bool, NetworkFailure>.Ok(false);
            }
        });
    }

    private static bool IsRecoveryRequest(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.EstablishSecrecyChannel => true,
            RpcServiceType.RestoreSecrecyChannel => true,
            RpcServiceType.RegisterAppDevice => true,
            _ => false
        };
    }

    private static bool IsUserInitiatedRequest(RpcServiceType serviceType)
    {
        return serviceType switch
        {
            RpcServiceType.ValidatePhoneNumber => true,
            RpcServiceType.VerifyOtp => true,
            RpcServiceType.InitiateVerification => true,
            RpcServiceType.OpaqueRegistrationInit => true,
            RpcServiceType.OpaqueRegistrationComplete => true,
            RpcServiceType.OpaqueSignInInitRequest => true,
            RpcServiceType.OpaqueSignInCompleteRequest => true,
            _ => false
        };
    }
}