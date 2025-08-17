using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Services.Rpc;

public class RpcServiceManager : IRpcServiceManager
{
    private readonly ISecrecyChannelRpcServices _secrecyChannelRpcServices;

    private readonly
        Dictionary<ServiceFlowType,
            Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>> _serviceInvokers;

    public RpcServiceManager(
        IUnaryRpcServices unaryRpcServices,
        IReceiveStreamRpcServices receiveStreamRpcServices,
        ISecrecyChannelRpcServices secrecyChannelRpcServices,
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
                    request.RpcServiceMethod, request.ReqId);
            }
            else
            {
                Log.Warning("Action {ServiceMethod} failed for req_id: {ReqId}. Error: {Error}",
                    request.RpcServiceMethod, request.ReqId, result.UnwrapErr().Message);
            }

            return result;
        }

        return Result<RpcFlow, NetworkFailure>.Err(NetworkFailure.InvalidRequestType("Unknown action type"));
    }
}