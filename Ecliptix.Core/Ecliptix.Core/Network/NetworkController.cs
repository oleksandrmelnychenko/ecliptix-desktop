using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public sealed class NetworkController(NetworkServiceManager networkServiceManager)
{
    private readonly ConcurrentDictionary<uint, EcliptixConnectionContext> _connections = new();

    public void CreateEcliptixConnectionContext(uint connectId, uint oneTimeKeyCount,
        PubKeyExchangeType pubKeyExchangeType)
    {
        EcliptixSystemIdentityKeys identityKeys = EcliptixSystemIdentityKeys.Create(oneTimeKeyCount).Unwrap();
        EcliptixProtocolSystem protocolSystem = new(identityKeys);

        EcliptixConnectionContext context = new()
        {
            PubKeyExchangeType = pubKeyExchangeType,
            EcliptixProtocolSystem = protocolSystem
        };

        _connections.TryAdd(connectId, context);
    }

    public async Task<Result<Unit, EcliptixProtocolFailure>> ExecuteServiceAction(
        uint connectId,
        RcpServiceAction serviceAction,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, EcliptixProtocolFailure>>> onSuccessCallback,
        CancellationToken token = default)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixConnectionContext? context))
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not found"));

        EcliptixProtocolSystem protocolSystem = context.EcliptixProtocolSystem;
        PubKeyExchangeType pubKeyExchangeType = context.PubKeyExchangeType;

        Result<CipherPayload, EcliptixProtocolFailure> outboundPayload =
            protocolSystem.ProduceOutboundMessage(plainBuffer);

        ServiceRequest request = ServiceRequest.New(flowType, serviceAction, outboundPayload.Unwrap(), []);
        var invokeResult =
            await networkServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr) return Result<Unit, EcliptixProtocolFailure>.Err(invokeResult.UnwrapErr());

        RpcFlow flow = invokeResult.Unwrap();
        switch (flow)
        {
            case RpcFlow.SingleCall singleCall:
                var callResult = await singleCall.Result;
                if (callResult.IsErr) return Result<Unit, EcliptixProtocolFailure>.Err(callResult.UnwrapErr());

                CipherPayload inboundPayload = callResult.Unwrap();
                var decryptedData =
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
                    var streamDecryptedData =
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

    public async Task<Result<Unit, EcliptixProtocolFailure>> DataCenterPubKeyExchange(
        uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixConnectionContext? context))
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Connection not found"));

        EcliptixProtocolSystem protocolSystem = context.EcliptixProtocolSystem;
        PubKeyExchangeType pubKeyExchangeType = context.PubKeyExchangeType;

        var pubKeyExchange =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, pubKeyExchangeType);

        PubKeyExchangeActionInvokable action = PubKeyExchangeActionInvokable.New(
            ServiceFlowType.Single,
            RcpServiceAction.DataCenterPubKeyExchange,
            pubKeyExchange.Unwrap(),
            peerPubKeyExchange => { protocolSystem.CompleteDataCenterPubKeyExchange(peerPubKeyExchange); });

        await networkServiceManager.BeginDataCenterPublicKeyExchange(action);

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }
}