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

    public async Task<Result<Unit, ShieldFailure>> ExecuteServiceAction(
        uint connectId,
        RcpServiceAction serviceAction,
        byte[] plainBuffer,
        ServiceFlowType flowType,
        Func<byte[], Task<Result<Unit, ShieldFailure>>> onSuccessCallback,
        CancellationToken token = default)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixConnectionContext? context))
            return Result<Unit, ShieldFailure>.Err(
                ShieldFailure.Generic("Connection not found"));

        EcliptixProtocolSystem protocolSystem = context.EcliptixProtocolSystem;
        PubKeyExchangeType pubKeyExchangeType = context.PubKeyExchangeType;

        CipherPayload outboundPayload =
            protocolSystem.ProduceOutboundMessage(connectId, pubKeyExchangeType, plainBuffer);

        ServiceRequest request = ServiceRequest.New(flowType, serviceAction, outboundPayload, []);
        Result<RpcFlow, ShieldFailure> invokeResult =
            await networkServiceManager.InvokeServiceRequestAsync(request, token);

        if (invokeResult.IsErr) return Result<Unit, ShieldFailure>.Err(invokeResult.UnwrapErr());

        RpcFlow flow = invokeResult.Unwrap();
        switch (flow)
        {
            case RpcFlow.SingleCall singleCall:
                Result<CipherPayload, ShieldFailure> callResult = await singleCall.Result;
                if (callResult.IsErr) return Result<Unit, ShieldFailure>.Err(callResult.UnwrapErr());

                CipherPayload inboundPayload = callResult.Unwrap();
                byte[] decryptedData =
                    protocolSystem.ProcessInboundMessage(connectId, pubKeyExchangeType, inboundPayload);
                Result<Unit, ShieldFailure> callbackOutcome = await onSuccessCallback(decryptedData);
                if (callbackOutcome.IsErr) return callbackOutcome;

                break;

            case RpcFlow.InboundStream inboundStream:
                await foreach (Result<CipherPayload, ShieldFailure> streamItem in inboundStream.Stream.WithCancellation(token))
                {
                    if (streamItem.IsErr)
                    {
                        Console.WriteLine($"Stream error: {streamItem.UnwrapErr().Message}");
                        continue;
                    }

                    CipherPayload streamPayload = streamItem.Unwrap();
                    byte[] streamDecryptedData =
                        protocolSystem.ProcessInboundMessage(connectId, pubKeyExchangeType, streamPayload);
                    Result<Unit, ShieldFailure> streamCallbackOutcome = await onSuccessCallback(streamDecryptedData);
                    if (streamCallbackOutcome.IsErr)
                        Console.WriteLine($"Callback error: {streamCallbackOutcome.UnwrapErr().Message}");
                }

                break;

            case RpcFlow.OutboundSink _:
            case RpcFlow.BidirectionalStream _:
                return Result<Unit, ShieldFailure>.Err(
                    ShieldFailure.Generic("Unsupported stream type"));
        }

        return Result<Unit, ShieldFailure>.Ok(Unit.Value);
    }

    public async Task<Result<Unit, ShieldFailure>> DataCenterPubKeyExchange(
        uint connectId)
    {
        if (!_connections.TryGetValue(connectId, out EcliptixConnectionContext? context))
            return Result<Unit, ShieldFailure>.Err(
                ShieldFailure.Generic("Connection not found"));

        EcliptixProtocolSystem protocolSystem = context.EcliptixProtocolSystem;
        PubKeyExchangeType pubKeyExchangeType = context.PubKeyExchangeType;

        PubKeyExchange pubKeyExchange =
            protocolSystem.BeginDataCenterPubKeyExchange(connectId, pubKeyExchangeType);

        PubKeyExchangeActionInvokable action = PubKeyExchangeActionInvokable.New(
            ServiceFlowType.Single,
            RcpServiceAction.DataCenterPubKeyExchange,
            pubKeyExchange,
            peerPubKeyExchange =>
            {
                protocolSystem.CompleteDataCenterPubKeyExchange(connectId, pubKeyExchangeType, peerPubKeyExchange);
            });

        await networkServiceManager.BeginDataCenterPublicKeyExchange(action);

        return Result<Unit, ShieldFailure>.Ok(Unit.Value);
    }
}