using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.ResilienceStrategy;
using Ecliptix.Core.Network.RpcServices;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
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

namespace Ecliptix.Core.Network.Providers;

public sealed class NetworkProvider(
    RpcServiceManager rpcServiceManager,
    ISecureStorageProvider secureStorageProvider,
    IRpcMetaDataProvider rpcMetaDataProvider,
    INetworkEvents networkEvents,
    ISystemEvents systemEvents) : INetworkProvider
{
    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();

    private const int DefaultOneTimeKeyCount = 5;

    private static readonly SemaphoreSlim SessionRecoveryLock = new(1, 1);
    private volatile bool _isSessionConsideredHealthy;

    private Option<ApplicationInstanceSettings> _applicationInstanceSettings = Option<ApplicationInstanceSettings>.None;

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings, uint connectId)
    {
        _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);

        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(DefaultOneTimeKeyCount).Unwrap();
        EcliptixProtocolSystem protocolSystem = new(identityKeys);

        _connections.TryAdd(connectId, protocolSystem);

        Guid appInstanceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId);
        Guid deviceId = Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId);

        rpcMetaDataProvider.SetAppInfo(appInstanceId, deviceId);
    }

    public void SetSecrecyChannelAsUnhealthy()
    {
        _isSessionConsideredHealthy = false;
        Log.Warning("Session has been programmatically marked as unhealthy");
    }

    public async Task<Result<Unit, NetworkFailure>> RestoreSecrecyChannelAsync()
    {
        await SessionRecoveryLock.WaitAsync();
        try
        {
            if (_isSessionConsideredHealthy)
            {
                Log.Information("Session was already recovered by another thread. Skipping redundant recovery");
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            Log.Information("Starting session recovery process...");
            Result<Unit, NetworkFailure> result = await PerformFullRecoveryLogic();

            _isSessionConsideredHealthy = result.IsOk;
            if (result.IsErr)
            {
                Log.Error(result.UnwrapErr().Message, "Session recovery failed.");
            }

            return result;
        }
        finally
        {
            SessionRecoveryLock.Release();
        }
    }

    // NEW ADDED MODIFIED 2025-07-07 16:12 by Vitalik Koliesnikov
    private async Task<Result<Unit, NetworkFailure>> PerformFullRecoveryLogic()
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Application instance settings not available"));
        }

        uint connectId = ComputeUniqueConnectId(_applicationInstanceSettings.Value!, PubKeyExchangeType.DataCenterEphemeralConnect);
        _connections.TryRemove(connectId, out _);
        Result<Option<byte[]>, InternalServiceApiFailure> storedStateResult =
            await secureStorageProvider.TryGetByKeyAsync(connectId.ToString());
        if (storedStateResult.IsOk && storedStateResult.Unwrap().HasValue)
        {
            EcliptixSecrecyChannelState? state =
                EcliptixSecrecyChannelState.Parser.ParseFrom(storedStateResult.Unwrap().Value);
            Result<bool, NetworkFailure> restoreResult =
                await RestoreSecrecyChannel(state, _applicationInstanceSettings.Value!);
            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                Log.Information("Session successfully restored from storage");
                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }

            Log.Warning("Failed to restore session from storage, falling back to reconnection");
        }
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

    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
    // public async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequest(
    //     uint connectId,
    //     RcpServiceType serviceType,
    //     byte[] plainBuffer,
    //     ServiceFlowType flowType,
    //     Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
    //     CancellationToken token = default)
    // {
    //     const int maxAttempts = 10;
    //     int attempt = 0;
    //
    //     while (attempt < maxAttempts)
    //     {
    //         attempt++;
    //
    //         if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
    //         {
    //             Log.Warning("Connection {ConnectId} not found on attempt {Attempt}", connectId, attempt);
    //             return Result<Unit, NetworkFailure>.Err(
    //                 NetworkFailure.InvalidRequestType("Connection not found"));
    //         }
    //
    //         ServiceRequest request = BuildRequest(protocolSystem, serviceType, plainBuffer, flowType);
    //         Result<Unit, NetworkFailure> result =
    //             await SendRequestAsync(connectId, protocolSystem, request, onCompleted, token);
    //
    //         if (result.IsOk)
    //         {
    //             if (attempt > 1)
    //             {
    //                 Log.Information("Request succeeded on attempt {Attempt}", attempt);
    //             }
    //             return result;
    //         }
    //
    //         NetworkFailure failure = result.UnwrapErr();
    //         Log.Warning("Request attempt {Attempt}/{MaxAttempts} failed: {Error}",
    //             attempt, maxAttempts, failure.Message);
    //
    //         // On failure, always attempt recovery except for the last attempt
    //         if (attempt < maxAttempts)
    //         {
    //             Log.Information("Attempting session recovery for service type: {ServiceType}", serviceType);
    //
    //             Result<Unit, NetworkFailure> recoveryResult =
    //                 await HandleRecoveryBasedOnServiceType(serviceType, connectId);
    //
    //             if (recoveryResult.IsOk)
    //             {
    //                 Log.Information("Recovery successful, retrying request");
    //                 continue;
    //             }
    //             else
    //             {
    //                 Log.Error("Recovery failed: {Error}", recoveryResult.UnwrapErr().Message);
    //             }
    //         }
    //
    //         if (attempt >= maxAttempts)
    //         {
    //             Log.Error("Request failed after {MaxAttempts} attempts. Final error: {Error}",
    //                 maxAttempts, failure.Message);
    //             return result;
    //         }
    //
    //         TimeSpan delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
    //         Log.Information("Retrying in {DelayMs}ms... (attempt {NextAttempt}/{MaxAttempts})",
    //             delay.TotalMilliseconds, attempt + 1, maxAttempts);
    //
    //         await Task.Delay(delay, token);
    //     }
    //
    //     return Result<Unit, NetworkFailure>.Err(
    //         NetworkFailure.InvalidRequestType($"Request failed after {maxAttempts} attempts"));
    // }
    
    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
    public async Task<Result<Unit, NetworkFailure>> ExecuteServiceRequest(
        uint connectId,
        RcpServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token = default)
    {
        return await ServiceSpecificResiliencePolicies.ExecuteWithRetryAndRecovery(
        operation: async () => {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Connection not found"));
        }

        ServiceRequest request = BuildRequest(protocolSystem, serviceType, plainBuffer, flowType);
        return await SendRequestAsync(connectId, protocolSystem, request, onCompleted, token);
        },
        recoveryHandler: HandleRecoveryBasedOnServiceType,
        serviceType: serviceType,
        connectId: connectId,
        maxAttempts: 3);
    }

    public async Task<Result<bool, NetworkFailure>> RestoreSecrecyChannel(
        EcliptixSecrecyChannelState ecliptixSecrecyChannelState,
        ApplicationInstanceSettings applicationInstanceSettings)
    {
        if (!_applicationInstanceSettings.HasValue)
        {
            _applicationInstanceSettings = Option<ApplicationInstanceSettings>.Some(applicationInstanceSettings);
        }

        rpcMetaDataProvider.SetAppInfo(Helpers.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Helpers.FromByteStringToGuid(applicationInstanceSettings.DeviceId));

        RestoreSecrecyChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse>.New(
                ServiceFlowType.Single,
                RcpServiceType.RestoreSecrecyChannel,
                request);

        Result<RestoreSecrecyChannelResponse, NetworkFailure> restoreAppDeviceSecrecyChannelResponse =
            await rpcServiceManager.RestoreAppDeviceSecrecyChannel(networkEvents, systemEvents, serviceRequest);

        if (restoreAppDeviceSecrecyChannelResponse.IsErr)
            return Result<bool, NetworkFailure>.Err(restoreAppDeviceSecrecyChannelResponse.UnwrapErr());

        RestoreSecrecyChannelResponse response = restoreAppDeviceSecrecyChannelResponse.Unwrap();

        if (response.Status == RestoreSecrecyChannelResponse.Types.RestoreStatus.SessionResumed)
        {
            Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> syncSecrecyChannelResult =
                SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

            return Result<bool, NetworkFailure>.Ok(true);
        }

        Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> g =
            SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

        return Result<bool, NetworkFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSecrecyChannelState, NetworkFailure>> EstablishSecrecyChannel(
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
                RcpServiceType.EstablishSecrecyChannel,
                pubKeyExchangeRequest.Unwrap());

        Result<PubKeyExchange, NetworkFailure> establishAppDeviceSecrecyChannelResult =
            await rpcServiceManager.EstablishAppDeviceSecrecyChannel(networkEvents, systemEvents, action);

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

    private Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> SyncSecrecyChannel(
        EcliptixSecrecyChannelState currentState,
        RestoreSecrecyChannelResponse serverResponse)
    {
        Result<EcliptixProtocolSystem, EcliptixProtocolFailure> systemResult = RecreateSystemFromState(currentState);
        if (systemResult.IsErr)
        {
            return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Err(systemResult.UnwrapErr());
        }

        EcliptixProtocolSystem system = systemResult.Unwrap();
        EcliptixProtocolConnection connection = system.GetConnection();

        Result<Unit, EcliptixProtocolFailure> syncResult = connection.SyncWithRemoteState(
            serverResponse.SendingChainLength,
            serverResponse.ReceivingChainLength
        );

        _connections.TryAdd(currentState.ConnectId, system);

        if (syncResult.IsErr)
        {
            return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Err(syncResult.UnwrapErr());
        }

        CreateStateFromSystem(currentState, system);

        return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Ok(currentState);
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

        if (connResult.IsErr)
        {
            idKeysResult.Unwrap().Dispose();
            return Result<EcliptixProtocolSystem, EcliptixProtocolFailure>.Err(connResult.UnwrapErr());
        }

        return EcliptixProtocolSystem.CreateFrom(idKeysResult.Unwrap(), connResult.Unwrap());
    }

    private static void CreateStateFromSystem(EcliptixSecrecyChannelState oldState, EcliptixProtocolSystem system)
    {
        system.GetConnection().ToProtoState().Map(newRatchetState =>
        {
            EcliptixSecrecyChannelState? newState = oldState.Clone();
            newState.RatchetState = newRatchetState;
            return newState;
        });
    }
    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
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

        if (_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            Result<EcliptixSecrecyChannelState, NetworkFailure> stateResult =
                await EstablishSecrecyChannel(connectId);

            if (stateResult.IsOk)
            {
                Result<Unit, InternalServiceApiFailure> storeResult =
                    await secureStorageProvider.StoreAsync(connectId.ToString(), stateResult.Unwrap().ToByteArray());

                if (storeResult.IsErr)
                {
                    Log.Warning("Failed to store reconnection session state: {Error}", storeResult.UnwrapErr());
                }
            }
        }

        Log.Information("Successfully reconnected and established new session");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
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
            await EstablishSecrecyChannel(connectId);

        if (establishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(establishResult.UnwrapErr());
        }

        Log.Information("Successfully established new connection");
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }
    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
    private ServiceRequest BuildRequest(
    EcliptixProtocolSystem protocolSystem,
    RcpServiceType serviceType,
    byte[] plainBuffer,
    ServiceFlowType flowType)
    {
    Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
        protocolSystem.ProduceOutboundMessage(plainBuffer);

    return ServiceRequest.New(flowType, serviceType, outboundPayload.Unwrap(), []);
    }
    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov

    private async Task<Result<Unit, NetworkFailure>> SendRequestAsync(
        uint connectId,
        EcliptixProtocolSystem protocolSystem,
        ServiceRequest request,
        Func<byte[], Task<Result<Unit, NetworkFailure>>> onCompleted,
        CancellationToken token)
    {
        Result<RpcFlow, NetworkFailure> invokeResult =
            await rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr) return Result<Unit, NetworkFailure>.Err(invokeResult.UnwrapErr());

        RpcFlow flow = invokeResult.Unwrap();
        switch (flow)
        {
            case RpcFlow.SingleCall singleCall:
                Result<CipherPayload, NetworkFailure> callResult = await singleCall.Result;
                if (callResult.IsErr) return Result<Unit, NetworkFailure>.Err(callResult.UnwrapErr());

                CipherPayload inboundPayload = callResult.Unwrap();
                Result<byte[], EcliptixProtocolFailure> decryptedData =
                    protocolSystem.ProcessInboundMessage(inboundPayload);
                if (decryptedData.IsErr) return Result<Unit, NetworkFailure>.Err(decryptedData.UnwrapErr().ToNetworkFailure());

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

    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
    private static bool IsSessionTimeoutError(NetworkFailure failure)
    {
        return failure.Message.Contains("not found or has timed out") ||
               failure.Message.Contains("session expired") ||
               failure.Message.Contains("connection lost");
    }

    // NEW ADDED 2025-07-07 16:12 by Vitalik Koliesnikov
    private async Task<Result<Unit, NetworkFailure>> HandleRecoveryBasedOnServiceType(
        RcpServiceType serviceType,
        uint connectId)
    {
        if (serviceType == RcpServiceType.RegisterAppDevice)
        {
            return await EstablishConnectionOnly(connectId);
        }
        else
        {
            return await PerformReconnectionLogic();
        }
    }
    
}