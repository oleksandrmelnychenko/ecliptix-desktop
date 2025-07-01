using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.ProtocolState;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public sealed class NetworkProvider(
    NetworkRpcServiceManager networkRpcServiceManager,
    IClientStateProvider clientStateProvider)
{
    private readonly ConcurrentDictionary<uint, EcliptixProtocolSystem> _connections = new();

    public void InitiateEcliptixProtocolSystem(ApplicationInstanceSettings applicationInstanceSettings,
        uint oneTimeKeyCount, uint connectId)
    {
        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(oneTimeKeyCount).Unwrap();
        EcliptixProtocolSystem protocolSystem = new(identityKeys);

        _connections.TryAdd(connectId, protocolSystem);

        clientStateProvider.SetClientInfo(Utilities.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Utilities.FromByteStringToGuid(applicationInstanceSettings.DeviceId));
    }

    public static uint ComputeUniqueConnectId(ApplicationInstanceSettings applicationInstanceSettings,
        PubKeyExchangeType pubKeyExchangeType) =>
        Utilities.ComputeUniqueConnectId(
            applicationInstanceSettings.AppInstanceId.Span,
            applicationInstanceSettings.DeviceId.Span,
            pubKeyExchangeType);

    public async Task<Result<Unit, EcliptixProtocolFailure>> ExecuteServiceRequest(
        uint connectId,
        RcpServiceType serviceType,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, EcliptixProtocolFailure>>> onSuccessCallback,
        CancellationToken token = default)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not found"));

        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);

        ServiceRequest request = ServiceRequest.New(flowType, serviceType, outboundPayload.Unwrap(), []);
        Result<RpcFlow, EcliptixProtocolFailure> invokeResult =
            await networkRpcServiceManager.InvokeServiceRequestAsync(request, token);

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
                Result<Unit, EcliptixProtocolFailure> callbackOutcome = await onSuccessCallback(decryptedData.Unwrap());
                if (callbackOutcome.IsErr) return callbackOutcome;

                break;

            case RpcFlow.InboundStream inboundStream:
                await foreach (Result<CipherPayload, EcliptixProtocolFailure> streamItem in
                               inboundStream.Stream.WithCancellation(token))
                {
                    if (streamItem.IsErr)
                    {
                        Console.WriteLine($"Stream error: {streamItem.UnwrapErr().Message}");
                        continue;
                    }

                    CipherPayload streamPayload = streamItem.Unwrap();
                    Result<byte[], EcliptixProtocolFailure> streamDecryptedData =
                        protocolSystem.ProcessInboundMessage(streamPayload);
                    Result<Unit, EcliptixProtocolFailure> streamCallbackOutcome =
                        await onSuccessCallback(streamDecryptedData.Unwrap());
                    if (streamCallbackOutcome.IsErr)
                        Console.WriteLine($"Callback error: {streamCallbackOutcome.UnwrapErr().Message}");
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
        clientStateProvider.SetClientInfo(Utilities.FromByteStringToGuid(applicationInstanceSettings.AppInstanceId),
            Utilities.FromByteStringToGuid(applicationInstanceSettings.DeviceId));

        RestoreSecrecyChannelRequest request = new();
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest =
            SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse>.New(
                ServiceFlowType.Single,
                RcpServiceType.RestoreSecrecyChannelState,
                request,
                onComplete: _ => { },
                onFailure: _ => { });

        Result<RestoreSecrecyChannelResponse, EcliptixProtocolFailure> responseResult =
            await networkRpcServiceManager.RestoreAppDeviceSecrecyChannel(serviceRequest);

        if (responseResult.IsErr) return responseResult.Map(_ => false);

        RestoreSecrecyChannelResponse response = responseResult.Unwrap();

        if (response.Status == RestoreSecrecyChannelResponse.Types.RestoreStatus.SessionResumed)
        {
            SyncSecrecyChannel(ecliptixSecrecyChannelState, response);
            return Result<bool, EcliptixProtocolFailure>.Ok(true);
        }

        SyncSecrecyChannel(ecliptixSecrecyChannelState, response);

        return Result<bool, EcliptixProtocolFailure>.Ok(false);
    }

    public async Task EstablishSecrecyChannel(uint connectId, Action<EcliptixSecrecyChannelState> onComplete,
        Action<EcliptixProtocolFailure> onFailure)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixProtocolSystem? protocolSystem))
        {
            Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not found"));
            return;
        }

        Result<PubKeyExchange, EcliptixProtocolFailure> pubKeyExchange =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, PubKeyExchangeType.DataCenterEphemeralConnect);

        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> action =
            SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange>.New(
                ServiceFlowType.Single,
                RcpServiceType.EstablishSecrecyChannel,
                pubKeyExchange.Unwrap(),
                peerPubKeyExchange =>
                {
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

                    if (ecliptixSecrecyChannelStateResult.IsOk)
                    {
                        onComplete(ecliptixSecrecyChannelStateResult.Unwrap());
                    }
                },
                onFailure: onFailure);

        await networkRpcServiceManager.EstablishAppDeviceSecrecyChannel(action);
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

        //system.CompleteDataCenterPubKeyExchange(currentState.PeerHandshakeMessage);
        
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