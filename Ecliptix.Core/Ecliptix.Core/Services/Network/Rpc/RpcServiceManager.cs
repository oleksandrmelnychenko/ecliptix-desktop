using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Services.Network.Rpc;

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
        INetworkEventService networkEvents, ISystemEventService systemEvents)
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

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        SecureEnvelope envelope,
        PubKeyExchangeType? exchangeType = null)
    {
        return await _secrecyChannelRpcServices.EstablishAppDeviceSecrecyChannelAsync(networkEvents,
            systemEvents,
            envelope,
            exchangeType);
    }

    public async Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        RestoreChannelRequest request)
    {
        return await _secrecyChannelRpcServices.RestoreAppDeviceSecrecyChannelAsync(networkEvents,
            systemEvents,
            request);
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