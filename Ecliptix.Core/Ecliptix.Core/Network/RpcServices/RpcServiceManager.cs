using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network.RpcServices;

public class RpcServiceManager(
    UnaryRpcServices unaryRpcServices,
    ReceiveStreamRpcServices receiveStreamRpcServices,
    SecrecyChannelRpcServices secrecyChannelRpcServices)
{
    private readonly ConcurrentDictionary<RcpServiceType, Task> _activeStreamHandles = new();

    public async Task EstablishAppDeviceSecrecyChannel(SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> serviceRequest)
    {
        Result<PubKeyExchange, EcliptixProtocolFailure> establishAppDeviceSecrecyChannelResult =
            await secrecyChannelRpcServices.EstablishAppDeviceSecrecyChannel(serviceRequest.PubKeyExchange);
        if (establishAppDeviceSecrecyChannelResult.IsOk)
        {
            serviceRequest.OnComplete(establishAppDeviceSecrecyChannelResult.Unwrap());
        }
        else
        {
            serviceRequest.OnFailure(establishAppDeviceSecrecyChannelResult.UnwrapErr());
        }
    }

    public async Task<Result<RestoreSecrecyChannelResponse, EcliptixProtocolFailure>> RestoreAppDeviceSecrecyChannel(
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest)
    {
        Result<RestoreSecrecyChannelResponse, EcliptixProtocolFailure> restoreSecrecyChannelResult =
            await secrecyChannelRpcServices.RestoreAppDeviceSecrecyChannelAsync(serviceRequest.PubKeyExchange);
        return restoreSecrecyChannelResult;
    }

    public async Task<Result<RpcFlow, EcliptixProtocolFailure>> InvokeServiceRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        RcpServiceType type = request.RcpServiceMethod;

        Result<RpcFlow, EcliptixProtocolFailure> result = default;
        switch (request.ActionType)
        {
            case ServiceFlowType.Single:
                result = await unaryRpcServices.InvokeRequestAsync(request, token);
                break;
            case ServiceFlowType.ReceiveStream:
                result = receiveStreamRpcServices.ProcessRequestAsync(request, token);
                break;
        }

        Console.WriteLine($"Action {type} executed successfully for req_id: {request.ReqId}");
        return result;
    }
}