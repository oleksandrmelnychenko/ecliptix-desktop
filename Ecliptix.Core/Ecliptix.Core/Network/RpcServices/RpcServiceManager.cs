using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.RpcServices;

public class RpcServiceManager
{
    private readonly SecrecyChannelRpcServices _secrecyChannelRpcServices;
    private readonly ConcurrentDictionary<RcpServiceType, Task> _activeStreamHandles = new();

    private readonly
        Dictionary<ServiceFlowType,
            Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>> _serviceInvokers;

    public RpcServiceManager(
        UnaryRpcServices unaryRpcServices,
        ReceiveStreamRpcServices receiveStreamRpcServices,
        SecrecyChannelRpcServices secrecyChannelRpcServices,
        INetworkEvents networkEvents, ISystemEvents systemEvents)
    {
        _secrecyChannelRpcServices = secrecyChannelRpcServices;

        _serviceInvokers =
            new Dictionary<ServiceFlowType,
                Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>>
            {
                {
                    ServiceFlowType.Single,
                    (req, token) => unaryRpcServices.InvokeRequestAsync(req, networkEvents, systemEvents, token)
                },
                {
                    ServiceFlowType.ReceiveStream, receiveStreamRpcServices.ProcessRequest
                }
            };
    }

    public async Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> serviceRequest)
    {
        return await _secrecyChannelRpcServices.EstablishAppDeviceSecrecyChannelAsync(networkEvents,
            systemEvents,
            serviceRequest.PubKeyExchange);
    }

    public async Task<Result<RestoreSecrecyChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest)
    {
        return await _secrecyChannelRpcServices.RestoreAppDeviceSecrecyChannelAsync(networkEvents,
            systemEvents,
            serviceRequest.PubKeyExchange);
    }

    public async Task<Result<RpcFlow, NetworkFailure>> InvokeServiceRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        if (_serviceInvokers.TryGetValue(request.ActionType,
                out Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>? invoker))
        {
            Result<RpcFlow, NetworkFailure> result = await invoker(request, token);
            
            if (result.IsOk)
            {
                Log.Debug("Action {ServiceMethod} executed successfully for req_id: {ReqId}", 
                    request.RcpServiceMethod, request.ReqId);
            }
            else
            {
                Log.Warning("Action {ServiceMethod} failed for req_id: {ReqId}. Error: {Error}", 
                    request.RcpServiceMethod, request.ReqId, result.UnwrapErr().Message);
            }
            
            return result;
        }

        return Result<RpcFlow, NetworkFailure>.Err(NetworkFailure.InvalidRequestType("Unknown action type"));
    }
}