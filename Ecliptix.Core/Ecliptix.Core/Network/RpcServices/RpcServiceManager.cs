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

    public async Task<PubKeyExchange> EstablishAppDeviceSecrecyChannel(
        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> serviceRequest)
    {
        return await secrecyChannelRpcServices.EstablishAppDeviceSecrecyChannel(serviceRequest.PubKeyExchange);
    }

    public async Task<RestoreSecrecyChannelResponse> RestoreAppDeviceSecrecyChannel(
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest)
    {
        return await secrecyChannelRpcServices.RestoreAppDeviceSecrecyChannelAsync(serviceRequest.PubKeyExchange);
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