using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Contracts.Core;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Network.Services;
using Ecliptix.Core.Network.Services.Retry;
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
using Ecliptix.Utilities.Failures.Validations;
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
    private readonly IConnectionStateManager _connectionStateManager;
    private readonly IPendingRequestManager _pendingRequestManager;

    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();

    private const int DefaultOneTimeKeyCount = 5;

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _inFlightRequests = new();

    private readonly Lock _cancellationLock = new();
    private CancellationTokenSource? _connectionRecoveryCts;
    
    // CRITICAL FIX: Shutdown cancellation token to avoid hanging on dispose
    private readonly CancellationTokenSource _shutdownCts = new();
    
    // CRITICAL FIX: Thundering herd protection for channel operations
    private readonly ConcurrentDictionary<uint, SemaphoreSlim> _channelGates = new();

    private readonly ConcurrentDictionary<uint, DateTime> _lastRecoveryAttempts = new();
    private readonly TimeSpan _recoveryThrottleInterval = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, DateTime> _lastRequestAttempts = new();
    private readonly TimeSpan _requestDebounceInterval = TimeSpan.FromMilliseconds(500);

    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    private int _outageState;
    private readonly Lock _outageLock = new();
    private TaskCompletionSource<bool> _outageRecoveredTcs = CreateOutageTcs();

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
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
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

        Log.Information("NetworkProvider initialized");
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
            deviceIdString,
            pubKeyExchangeType);

        Log.Debug(
            "[CLIENT] ConnectId Calculation: AppInstanceId={AppInstanceId}, DeviceId={DeviceId}, ContextType={ContextType}, ConnectId={ConnectId}",
            appInstanceIdString, deviceIdString, pubKeyExchangeType, connectId);

        return connectId;
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

    public void ClearConnection(uint connectId)
    {
        if (!_connections.TryRemove(connectId, out EcliptixProtocolSystem? system)) return;
        system.Dispose();
        _connectionStateManager.RemoveConnection(connectId);
        Log.Information("Cleared connection {ConnectId} from cache and monitoring", connectId);
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

        if (!waitForRecovery)
        {
            DateTime now = DateTime.UtcNow;
            string debounceKey = $"{connectId}_{serviceType}";
            if (_lastRequestAttempts.TryGetValue(debounceKey, out DateTime lastAttempt))
            {
                TimeSpan timeSinceLastRequest = now - lastAttempt;
                if (timeSinceLastRequest < _requestDebounceInterval)
                {
                    Log.Debug("Request debounced for {ServiceType} (last attempt {TimeSince} ago)",
                        serviceType, timeSinceLastRequest);
                    return Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType("Request too frequent, please wait"));
                }
            }

            _lastRequestAttempts.AddOrUpdate(debounceKey, now, (_, _) => now);
        }

        bool shouldAllowDuplicates = allowDuplicates || ShouldAllowDuplicateRequests(serviceType);
        var perRequestCts = new CancellationTokenSource();
        if (!shouldAllowDuplicates && !_inFlightRequests.TryAdd(requestKey, perRequestCts))
        {
            perRequestCts.Dispose();
            Log.Debug("Duplicate request detected for {ServiceType}, rejecting", serviceType);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Duplicate request rejected"));
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, perRequestCts.Token);
        CancellationToken operationToken = linkedCts.Token;

        try
        {
            await WaitForOutageRecoveryAsync(operationToken, waitForRecovery).ConfigureAwait(false);

            string operationName = $"{serviceType}";
            Result<Unit, NetworkFailure> networkResult = await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                operation: async () =>
                {
                    operationToken.ThrowIfCancellationRequested();

                    if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
                    {
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(
                                "Connection unavailable - server may be recovering"));
                    }

                    Result<ServiceRequest, NetworkFailure> requestResult =
                        BuildRequest(protocolSystem, serviceType, plainBuffer, flowType);
                    if (requestResult.IsErr)
                    {
                        NetworkFailure buildFailure = requestResult.UnwrapErr();
                        if (FailureClassification.IsServerShutdown(buildFailure))
                            EnterOutage(buildFailure.Message, connectId);

                        return Result<Unit, NetworkFailure>.Err(buildFailure);
                    }

                    ServiceRequest request = requestResult.Unwrap();
                    uint originalReqId = request.ReqId; // Preserve for proper replay

                    try
                    {
                        Result<Unit, NetworkFailure> result = flowType switch
                        {
                            ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, request, onCompleted,
                                operationToken).ConfigureAwait(false),
                            ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem,
                                request, onCompleted, operationToken).ConfigureAwait(false),
                            ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem, request,
                                onCompleted, operationToken).ConfigureAwait(false),
                            ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(
                                protocolSystem, request, onCompleted, operationToken).ConfigureAwait(false),
                            _ => Result<Unit, NetworkFailure>.Err(
                                NetworkFailure.InvalidRequestType($"Unsupported flow type: {flowType}"))
                        };

                        if (!result.IsErr) return result;
                        NetworkFailure failure = result.UnwrapErr();

                        if (FailureClassification.IsServerShutdown(failure))
                        {
                            EnterOutage(failure.Message, connectId);

                            string requestId = originalReqId.ToString();
                            // CRITICAL FIX: Store rebuild function, not stale encrypted request
                            _pendingRequestManager.RegisterPendingRequest(requestId, async () =>
                            {
                                await ReExecuteServiceFromPlaintextAsync(connectId, serviceType, plainBuffer, flowType, onCompleted, originalReqId, token).ConfigureAwait(false);
                            });
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
            if (!shouldAllowDuplicates && _inFlightRequests.TryRemove(requestKey, out var cts))
            {
                cts.Dispose();
            }
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

        Result<byte[], SecureStorageFailure> stateResult =
            await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

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
            Log.Information("Session restoration completed for {ConnectId}", connectId);
            ExitOutage();
            ResetRetryStrategyAfterOutage();

            await RetryPendingRequestsAfterRecovery().ConfigureAwait(false);

            return Result<Unit, NetworkFailure>.Ok(Unit.Value);
        }

        Log.Warning("Restoration failed, falling back to reconnection");
        Result<Unit, NetworkFailure> reconnectionResult = await PerformReconnectionLogic();
        if (reconnectionResult.IsErr)
        {
            return reconnectionResult;
        }

        Log.Information("Session successfully re-established via reconnection");

        ExitOutage();
        ResetRetryStrategyAfterOutage();

        await RetryPendingRequestsAfterRecovery();

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
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

    private async Task RetryPendingRequestsAfterRecovery()
    {
        try
        {
            Log.Information("Retrying pending requests after successful connection recovery");

            await _pendingRequestManager.RetryAllPendingRequestsAsync();

            Log.Information("Completed retrying pending requests");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error while retrying pending requests after recovery");
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

        Result<EcliptixSecrecyChannelState, NetworkFailure> establishResult =
            await EstablishSecrecyChannelAsync(connectId);

        if (establishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        EcliptixSecrecyChannelState secrecyChannelState = establishResult.Unwrap();

        Result<Unit, SecureStorageFailure> saveResult = await _secureProtocolStateStorage.SaveStateAsync(
            secrecyChannelState.ToByteArray(), connectId.ToString());

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

        Log.Information("Successfully established new connection");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
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
            Result<Unit, EcliptixProtocolFailure>
                syncResult = SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
            return syncResult.IsErr
                ? Result<bool, NetworkFailure>.Err(syncResult.UnwrapErr().ToNetworkFailure())
                : Result<bool, NetworkFailure>.Ok(true);
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
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, PubKeyExchangeType.DataCenterEphemeralConnect);

        if (pubKeyExchangeRequest.IsErr)
        {
            return Result<EcliptixSecrecyChannelState, NetworkFailure>.Err(
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
            return Result<EcliptixSecrecyChannelState, NetworkFailure>.Err(establishAppDeviceSecrecyChannelResult
                .UnwrapErr());
        }

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
        Log.Debug("SendUnaryRequestAsync - ServiceType: {ServiceType}", request.RpcServiceMethod);
        Result<RpcFlow, NetworkFailure> invokeResult =
            await _rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr)
        {
            Log.Warning("InvokeServiceRequestAsync failed: {Error}", invokeResult.UnwrapErr().Message);
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

        Log.Debug("Successfully decrypted unary response");
        await onCompleted(decryptedData.Unwrap());
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, NetworkFailure>> ReExecuteServiceRequestAsync(
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        uint connectId,
        CancellationToken token)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Connection unavailable - server may be recovering"));
        }

        Result<Unit, NetworkFailure> result = request.ActionType switch
        {
            ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(protocolSystem, request, onCompleted, token),
            _ => Result<Unit, NetworkFailure>.Err(NetworkFailure.InvalidRequestType($"Unsupported flow type: {request.ActionType}"))
        };

        return result;
    }

    // CRITICAL FIX: Re-execute from plaintext with fresh encryption
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

        // Rebuild request with fresh encryption
        Result<ServiceRequest, NetworkFailure> requestResult =
            BuildRequest(protocolSystem, serviceType, plainBuffer, flowType);
        
        if (requestResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(requestResult.UnwrapErr());
        }

        ServiceRequest request = requestResult.Unwrap();
        
        // TODO: If ServiceRequest has WithReqId method, use it to preserve originalReqId
        Log.Information("Re-executing request with fresh encryption - original ReqId {OriginalReqId}, new ReqId {NewReqId}", 
            originalReqId, request.ReqId);

        Result<Unit, NetworkFailure> result = request.ActionType switch
        {
            ServiceFlowType.Single => await SendUnaryRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.ReceiveStream => await SendReceiveStreamRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.SendStream => await SendSendStreamRequestAsync(protocolSystem, request, onCompleted, token),
            ServiceFlowType.BidirectionalStream => await SendBidirectionalStreamRequestAsync(protocolSystem, request, onCompleted, token),
            _ => Result<Unit, NetworkFailure>.Err(NetworkFailure.InvalidRequestType($"Unsupported flow type: {request.ActionType}"))
        };

        if (result.IsOk)
        {
            Log.Information("Successfully re-executed service request from plaintext after connection recovery");
        }

        return result;
    }

    private async Task<Result<Unit, NetworkFailure>> SendReceiveStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Log.Debug("SendReceiveStreamRequestAsync - ServiceType: {ServiceType}", request.RpcServiceMethod);
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

            Log.Debug("Successfully decrypted stream item");
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
        Log.Debug("SendSendStreamRequestAsync - ServiceType: {ServiceType}", request.RpcServiceMethod);
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

        // TODO: Implement client streaming logic when needed
        return Result<Unit, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Client streaming is not yet implemented"));
    }

    private async Task<Result<Unit, NetworkFailure>> SendBidirectionalStreamRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onStreamItem,
        CancellationToken token)
    {
        Log.Debug("SendBidirectionalStreamRequestAsync - ServiceType: {ServiceType}", request.RpcServiceMethod);
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

        // TODO: Implement bidirectional streaming logic when needed
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

        // CRITICAL FIX: Add timeout and shutdown cancellation to prevent infinite hangs
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token, _shutdownCts.Token);
        cts.CancelAfter(TimeSpan.FromSeconds(30)); // Configurable max wait
        
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

        Log.Warning("Server appears unavailable/shutdown (ConnectId: {ConnectId}). Entering outage mode: {Reason}",
            connectId, reason);

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

        _networkEvents.InitiateChangeState(
            NetworkStatusChangedEvent.New(NetworkStatus.ConnectionRestored)
        );

        Log.Information("Outage cleared. Secrecy channel is healthy; resuming requests");
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

        // Actually cancel the operations, not just remove dictionary entries
        foreach (var kv in _inFlightRequests.ToArray())
        {
            if (_inFlightRequests.TryRemove(kv.Key, out var cts))
            {
                try
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // Already disposed, ignore
                }
            }
        }
    }

    // CRITICAL FIX: Single-flight guard for channel operations
    private async Task<T> WithChannelGate<T>(uint connectId, Func<Task<T>> action)
    {
        var gate = _channelGates.GetOrAdd(connectId, _ => new SemaphoreSlim(1, 1));
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

            if (!_applicationInstanceSettings.HasValue)
            {
                Log.Warning("Cannot recover connection {ConnectId} - application settings not available", connectId);
                return;
            }

            Result<byte[], SecureStorageFailure> stateResult =
                await _secureProtocolStateStorage.LoadStateAsync(connectId.ToString());

            bool restorationSuccessful;
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
                Result<EcliptixSecrecyChannelState, NetworkFailure> newResult =
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

                            Result<Unit, SecureStorageFailure> saveResult =
                                await _secureProtocolStateStorage.SaveStateAsync(
                                    state.ToByteArray(), connectId.ToString());

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

                            Result<Unit, SecureStorageFailure> saveResult =
                                await _secureProtocolStateStorage.SaveStateAsync(
                                    state.ToByteArray(), connectId.ToString());

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
                // CRITICAL FIX: Signal shutdown and cancel outage waiters properly
                _shutdownCts.Cancel();
                
                _connectionStateManager.Dispose();

                // Cancel all in-flight requests properly
                foreach (var kv in _inFlightRequests.ToArray())
                {
                    if (_inFlightRequests.TryRemove(kv.Key, out var cts))
                    {
                        try
                        {
                            cts.Cancel();
                            cts.Dispose();
                        }
                        catch (ObjectDisposedException) { }
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

                // Dispose channel gates
                foreach (var gate in _channelGates.Values)
                {
                    gate.Dispose();
                }
                _channelGates.Clear();

                // Dispose shutdown token
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
        Log.Information("Starting protocol resynchronization for connection {ConnectId}", connectId);

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

            Result<EcliptixSecrecyChannelState, NetworkFailure> establishResult =
                await EstablishSecrecyChannelAsync(connectId);

            if (establishResult.IsErr)
            {
                Log.Error("Failed to re-establish secrecy channel during resynchronization: {Error}",
                    establishResult.UnwrapErr());
                return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
            }

            EcliptixSecrecyChannelState newState = establishResult.Unwrap();
            Result<Unit, SecureStorageFailure> saveResult = await _secureProtocolStateStorage.SaveStateAsync(
                newState.ToByteArray(), connectId.ToString());

            if (saveResult.IsOk)
            {
                Log.Information("Protocol resynchronization completed successfully for connection {ConnectId}",
                    connectId);
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
        Log.Information("Manual retry requested. Forcing fresh protocol establishment");
        return await PerformAdvancedRecoveryLogic();
    }

    private async Task<Result<Unit, NetworkFailure>> PerformFreshProtocolEstablishment(uint connectId)
    {
        Log.Information("Starting fresh protocol establishment for connection {ConnectId}", connectId);

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
                    Log.Debug("Disposed old protocol system for fresh establishment");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error disposing old protocol system");
                }
            }

            await _secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());
            Log.Debug("Deleted all stored protocol state for fresh establishment");

            InitiateEcliptixProtocolSystem(_applicationInstanceSettings.Value!, connectId);

            Result<EcliptixSecrecyChannelState, NetworkFailure> establishResult =
                await EstablishSecrecyChannelAsync(connectId);

            if (establishResult.IsErr)
            {
                Log.Error("Failed fresh protocol establishment: {Error}", establishResult.UnwrapErr());
                return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
            }

            EcliptixSecrecyChannelState freshState = establishResult.Unwrap();
            Result<Unit, SecureStorageFailure> saveResult = await _secureProtocolStateStorage.SaveStateAsync(
                freshState.ToByteArray(), connectId.ToString());

            if (saveResult.IsOk)
            {
                Log.Information("Fresh protocol establishment completed successfully for connection {ConnectId}",
                    connectId);
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
        Log.Information("Resetting retry strategy state after successful outage recovery");

        _retryStrategy.ResetConnectionState();

        foreach (KeyValuePair<uint, EcliptixProtocolSystem> connection in _connections)
        {
            _retryStrategy.MarkConnectionHealthy(connection.Key);
            Log.Debug("Marked connection {ConnectId} as healthy after outage recovery", connection.Key);
        }

        _lastRecoveryAttempts.Clear();

        Log.Debug("Retry strategy state cleared - fresh retry cycles will begin for subsequent operations");
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem _))
            return false;

        // CRITICAL FIX: Use actual ConnectionStateManager health status
        var health = _connectionStateManager.GetConnectionHealth(connectId);
        return health?.Status == ConnectionHealthStatus.Healthy;
    }

    public async Task<Result<bool, NetworkFailure>> TryRestoreConnectionAsync(uint connectId)
    {
        // CRITICAL FIX: Single-flight protection against concurrent restore operations
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
                EcliptixSecrecyChannelState state = EcliptixSecrecyChannelState.Parser.ParseFrom(stateBytes);
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
}