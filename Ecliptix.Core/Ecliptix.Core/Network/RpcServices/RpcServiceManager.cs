using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Core.Network.RpcServices;

public class RpcServiceManager
{
    private readonly UnaryRpcServices _unaryRpcServices;
    private readonly ReceiveStreamRpcServices _receiveStreamRpcServices;
    private readonly SecrecyChannelRpcServices _secrecyChannelRpcServices;
    private readonly ConcurrentDictionary<RcpServiceType, Task> _activeStreamHandles = new();

    private readonly
        Dictionary<ServiceFlowType,
            Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, EcliptixProtocolFailure>>>> _serviceInvokers;

    public RpcServiceManager(
        UnaryRpcServices unaryRpcServices,
        ReceiveStreamRpcServices receiveStreamRpcServices,
        SecrecyChannelRpcServices secrecyChannelRpcServices)
    {
        _unaryRpcServices = unaryRpcServices;
        _receiveStreamRpcServices = receiveStreamRpcServices;
        _secrecyChannelRpcServices = secrecyChannelRpcServices;

        _serviceInvokers =
            new Dictionary<ServiceFlowType,
                Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, EcliptixProtocolFailure>>>>
            {
                { ServiceFlowType.Single, (req, token) => _unaryRpcServices.InvokeRequestAsync(req, token) },
                { ServiceFlowType.ReceiveStream, (req, token) =>
                     _receiveStreamRpcServices.ProcessRequest(req, token) }
            };
    }

    public async Task<PubKeyExchange> EstablishAppDeviceSecrecyChannel(
        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> serviceRequest)
    {
        return await _secrecyChannelRpcServices.EstablishAppDeviceSecrecyChannel(serviceRequest.PubKeyExchange);
    }

    public async Task<RestoreSecrecyChannelResponse> RestoreAppDeviceSecrecyChannel(
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest)
    {
        return await _secrecyChannelRpcServices.RestoreAppDeviceSecrecyChannelAsync(serviceRequest.PubKeyExchange);
    }

    public async Task<Result<RpcFlow, EcliptixProtocolFailure>> InvokeServiceRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        if (_serviceInvokers.TryGetValue(request.ActionType, out var invoker))
        {
            Result<RpcFlow, EcliptixProtocolFailure> result = await invoker(request, token);
            if (result.IsOk)
            {
                Console.WriteLine(
                    $"Action {request.RcpServiceMethod} executed successfully for req_id: {request.ReqId}");
            }
            else
            {
                Console.WriteLine(
                    $"Action {request.RcpServiceMethod} failed for req_id: {request.ReqId}. Error: {result.UnwrapErr().Message}");
            }

            return result;
        }
        else
        {
            return Result<RpcFlow, EcliptixProtocolFailure>.Err(EcliptixProtocolFailure.Generic("Unknown action type"));
        }
    }
}