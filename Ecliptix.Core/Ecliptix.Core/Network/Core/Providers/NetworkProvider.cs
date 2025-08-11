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

    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();

    private const int DefaultOneTimeKeyCount = 5;

    private volatile bool _isSecrecyChannelConsideredHealthy;

    private readonly ConcurrentDictionary<string, byte> _inFlightRequests = new();

    private readonly Lock _cancellationLock = new();
    private CancellationTokenSource? _connectionRecoveryCts;

    private readonly ConcurrentDictionary<uint, DateTime> _lastRecoveryAttempts = new();
    private readonly TimeSpan _recoveryThrottleInterval = TimeSpan.FromSeconds(10);

    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    private volatile int _outageState;
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
        IConnectionStateManager connectionStateManager)
    {
        _rpcServiceManager = rpcServiceManager;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _secureProtocolStateStorage = secureProtocolStateStorage;
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _networkEvents = networkEvents;
        _systemEvents = systemEvents;
        _retryStrategy = retryStrategy;
        _connectionStateManager = connectionStateManager;

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
        PubKeyExchangeType pubKeyExchangeType) =>
        Helpers.ComputeUniqueConnectId(
            applicationInstanceSettings.AppInstanceId.Span,
            applicationInstanceSettings.DeviceId.Span,
            pubKeyExchangeType);

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

    public async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequestAsync(
        uint connectId,
        RpcServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        bool allowDuplicates = false,
        CancellationToken token = default)
    {
        if (_disposed)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("NetworkProvider is disposed"));
        }

        string hex = Convert.ToHexString(plainBuffer);
        string prefix = hex[..Math.Min(hex.Length, 16)];
        string requestKey = $"{connectId}_{serviceType}_{prefix}";

        bool shouldAllowDuplicates = allowDuplicates || ShouldAllowDuplicateRequests(serviceType);
        if (!shouldAllowDuplicates && !_inFlightRequests.TryAdd(requestKey, 0))
        {
            Log.Debug("Duplicate request detected for {ServiceType}, rejecting", serviceType);
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Duplicate request rejected"));
        }

        CancellationToken recoveryToken = GetConnectionRecoveryToken();
        using CancellationTokenSource combinedCts =
            CancellationTokenSource.CreateLinkedTokenSource(token, recoveryToken);
        CancellationToken combinedToken = combinedCts.Token;

        try
        {
            await WaitForOutageRecoveryAsync(combinedToken);

            string operationName = $"{serviceType}";
            Result<Unit, NetworkFailure> networkResult = await _retryStrategy.ExecuteSecrecyChannelOperationAsync(
                operation: async () =>
                {
                    if (combinedToken.IsCancellationRequested)
                    {
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding("Request cancelled"));
                    }

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

                    try
                    {
                        Result<Unit, NetworkFailure> result =
                            await SendRequestAsync(protocolSystem, request, onCompleted, combinedToken);

                        if (!result.IsErr) return result;
                        NetworkFailure failure = result.UnwrapErr();

                        if (FailureClassification.IsServerShutdown(failure))
                        {
                            EnterOutage(failure.Message, connectId);
                        }
                        else if (FailureClassification.IsCryptoDesync(failure))
                        {
                            Log.Warning(
                                "Cryptographic desync detected for connection {ConnectId}, initiating recovery",
                                connectId);
                            if (!ShouldThrottleRecovery(connectId))
                            {
                                _lastRecoveryAttempts.AddOrUpdate(connectId, DateTime.UtcNow,
                                    (_, _) => DateTime.UtcNow);
                                ExecuteBackgroundTask(PerformAdvancedRecoveryLogic,
                                    $"CryptographicRecovery-{connectId}");
                            }
                        }
                        else
                        {
                            return result;
                        }

                        _connectionStateManager.UpdateConnectionHealth(connectId, result);

                        return result;
                    }
                    catch (OperationCanceledException)
                    {
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding("Request cancelled"));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Exception during request execution for {ServiceType}", serviceType);
                        return Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding($"Request execution failed: {ex.Message}"));
                    }
                },
                operationName: operationName,
                connectId: connectId,
                maxRetries: 10,
                cancellationToken: combinedToken);

            return networkResult;
        }
        finally
        {
            if (!shouldAllowDuplicates)
            {
                _inFlightRequests.TryRemove(requestKey, out _);
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
            Log.Information("Session restoration completed for {ConnectId}", connectId);
            ExitOutage();
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

    private async Task<Result<Unit, NetworkFailure>> SendRequestAsync(
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token)
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

                await onCompleted(decryptedData.Unwrap());
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

                    await onCompleted(streamDecryptedData.Unwrap());
                }

                break;

            case RpcFlow.OutboundSink _:
            case RpcFlow.BidirectionalStream _:
                return Result<Unit, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType("Unsupported stream type"));
        }

        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private async Task WaitForOutageRecoveryAsync(CancellationToken token)
    {
        if (Volatile.Read(ref _outageState) == 0) return;

        Task waitTask;
        lock (_outageLock)
        {
            waitTask = _outageRecoveredTcs.Task;
        }

        await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, token));
        token.ThrowIfCancellationRequested();
        await waitTask;
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

        ExecuteBackgroundTask(() => ServerShutdownRecoveryLoop(connectId), $"ServerShutdownRecovery-{connectId}");
    }

    private void ExitOutage()
    {
        if (Interlocked.Exchange(ref _outageState, 0) == 0) return;

        lock (_outageLock)
        {
            if (!_outageRecoveredTcs.Task.IsCompleted)
                _outageRecoveredTcs.TrySetResult(true);
        }

        Log.Information("Outage cleared. Secrecy channel is healthy; resuming requests");
    }

    private async Task ServerShutdownRecoveryLoop(uint connectId)
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

        foreach (string key in _inFlightRequests.Keys.ToList())
        {
            _inFlightRequests.TryRemove(key, out _);
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

            string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
            Result<byte[], SecureStorageFailure> stateResult =
                await _secureProtocolStateStorage.LoadStateAsync(userId);

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

                            string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
                            Result<Unit, SecureStorageFailure> saveResult =
                                await _secureProtocolStateStorage.SaveStateAsync(
                                    state.ToByteArray(), userId);

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

                            string userId = _applicationInstanceSettings.Value!.AppInstanceId.ToStringUtf8();
                            Result<Unit, SecureStorageFailure> saveResult =
                                await _secureProtocolStateStorage.SaveStateAsync(
                                    state.ToByteArray(), userId);

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
                _connectionStateManager.Dispose();

                _inFlightRequests.Clear();
                _lastRecoveryAttempts.Clear();

                lock (_outageLock)
                {
                    _outageRecoveredTcs.TrySetResult(false);
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

                Log.Information("NetworkProvider disposed safely");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during NetworkProvider disposal");
            }
        }
    }
}