using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.RpcServices;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.ResilienceStrategy;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Network.Providers;

public sealed class NetworkProvider(
    RpcServiceManager rpcServiceManager,
    ISecureStorageProvider secureStorageProvider,
    IRpcMetaDataProvider rpcMetaDataProvider) : ISessionManager
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

    public void MarkSessionAsUnhealthy()
    {
        _isSessionConsideredHealthy = false;
        Log.Warning("Session has been programmatically marked as unhealthy");
    }

    public async Task<Result<Unit, EcliptixProtocolFailure>> ReEstablishSessionAsync()
    {
        await SessionRecoveryLock.WaitAsync();
        try
        {
            if (_isSessionConsideredHealthy)
            {
                Log.Information("Session was already recovered by another thread. Skipping redundant recovery");
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }

            Log.Information("Starting session recovery process...");
            Result<Unit, EcliptixProtocolFailure> result = await PerformFullRecoveryLogic();

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

    private async Task<Result<Unit, EcliptixProtocolFailure>> PerformFullRecoveryLogic()
    {
        uint connectId =
            ComputeUniqueConnectId(_applicationInstanceSettings.Value!, PubKeyExchangeType.DataCenterEphemeralConnect);
        _connections.TryRemove(connectId, out _);

        Result<Option<byte[]>, InternalServiceApiFailure> storedStateResult =
            await secureStorageProvider.TryGetByKeyAsync(connectId.ToString());
        if (storedStateResult.IsOk && storedStateResult.Unwrap().HasValue)
        {
            EcliptixSecrecyChannelState? state =
                EcliptixSecrecyChannelState.Parser.ParseFrom(storedStateResult.Unwrap().Value);
            Result<bool, EcliptixProtocolFailure> restoreResult =
                await RestoreSecrecyChannel(state, _applicationInstanceSettings.Value!);
            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                Log.Information("Session successfully restored from storage");
                return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
            }

            Log.Warning("Failed to restore session from storage, will attempt full re-establishment");
        }

        InitiateEcliptixProtocolSystem(_applicationInstanceSettings.Value!, connectId);

        Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> establishResult =
            await EstablishSecrecyChannel(connectId);
        if (establishResult.IsErr)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(establishResult.UnwrapErr());
        }

        Result<Unit, InternalServiceApiFailure> storeResult =
            await secureStorageProvider.StoreAsync(connectId.ToString(), establishResult.Unwrap().ToByteArray());
        if (storeResult.IsErr)
        {
            Log.Error("Failed to store newly established session state: {Error}", storeResult.UnwrapErr());
        }

        Log.Information("Session successfully established via new key exchange");
        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType) =>
        Helpers.ComputeUniqueConnectId(
            applicationInstanceSettings.AppInstanceId.Span,
            applicationInstanceSettings.DeviceId.Span,
            pubKeyExchangeType);

    public async Task<Result<Unit, EcliptixProtocolFailure>> ExecuteServiceRequest(
        uint connectId,
        RcpServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, EcliptixProtocolFailure>>> onCompleted,
        CancellationToken token = default)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not found"));
        }

        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);

        ServiceRequest request = ServiceRequest.New(flowType, serviceType, outboundPayload.Unwrap(), []);
        Result<RpcFlow, EcliptixProtocolFailure> invokeResult =
            await rpcServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr) return Result<Unit, EcliptixProtocolFailure>.Err(invokeResult.UnwrapErr());

        RpcFlow flow = invokeResult.Unwrap();
        switch (flow)
        {
            case RpcFlow.SingleCall singleCall:
                Result<CipherPayload, EcliptixProtocolFailure> callResult = await singleCall.Result;
                if (callResult.IsErr) return Result<Unit, EcliptixProtocolFailure>.Err(callResult.UnwrapErr());

                CipherPayload inboundPayload = callResult.Unwrap();
                Result<byte[], EcliptixProtocolFailure> decryptedData =
                    protocolSystem.ProcessInboundMessage(inboundPayload);
                Result<Unit, EcliptixProtocolFailure> callbackOutcome = await onCompleted(decryptedData.Unwrap());
                if (callbackOutcome.IsErr) return callbackOutcome;

                break;

            case RpcFlow.InboundStream inboundStream:
                await foreach (Result<CipherPayload, EcliptixProtocolFailure> streamItem in
                               inboundStream.Stream.WithCancellation(token))
                {
                    if (streamItem.IsErr)
                    {
                        continue;
                    }

                    CipherPayload streamPayload = streamItem.Unwrap();
                    Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                        protocolSystem.ProcessInboundMessage(streamPayload);
                    Result<Unit, EcliptixProtocolFailure> streamCallbackOutcome =
                        await onCompleted(streamDecryptedData.Unwrap());
                    if (streamCallbackOutcome.IsErr)
                    {
                    }
                }

                break;

            case RpcFlow.OutboundSink _:
            case RpcFlow.BidirectionalStream _:
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Unsupported stream type"));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public async Task<Result<bool, EcliptixProtocolFailure>> RestoreSecrecyChannel(
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

        RestoreSecrecyChannelResponse response =
            await rpcServiceManager.RestoreAppDeviceSecrecyChannel(serviceRequest);

        if (response.Status == RestoreSecrecyChannelResponse.Types.RestoreStatus.SessionResumed)
        {
            SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
            return Result<bool, EcliptixProtocolFailure>.Ok(true);
        }

        SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

        return Result<bool, EcliptixProtocolFailure>.Ok(false);
    }

    public async Task<Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>> EstablishSecrecyChannel(
        uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            return Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not found"));
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchangeRequest =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, PubKeyExchangeType.DataCenterEphemeralConnect);

        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> action =
            SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange>.New(
                ServiceFlowType.Single,
                RcpServiceType.EstablishSecrecyChannel,
                pubKeyExchangeRequest.Unwrap());

        PubKeyExchange peerPubKeyExchange =
            await rpcServiceManager.EstablishAppDeviceSecrecyChannel(action);
       
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

        return ecliptixSecrecyChannelStateResult;
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

        return CreateStateFromSystem(currentState, system);
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

    private static Result<EcliptixSecrecyChannelState, EcliptixProtocolFailure> CreateStateFromSystem(
        EcliptixSecrecyChannelState oldState, EcliptixProtocolSystem system)
    {
        return system.GetConnection().ToProtoState().Map(newRatchetState =>
        {
            EcliptixSecrecyChannelState? newState = oldState.Clone();
            newState.RatchetState = newRatchetState;
            return newState;
        });
    }
}