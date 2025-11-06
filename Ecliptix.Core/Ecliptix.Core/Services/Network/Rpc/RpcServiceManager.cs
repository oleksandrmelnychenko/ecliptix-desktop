using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Rpc;

internal class RpcServiceManager : IRpcServiceManager
{
    private readonly ISecrecyChannelRpcServices _secrecyChannelRpcServices;

    private readonly
        Dictionary<ServiceFlowType,
            Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>> _serviceInvokers;

    public RpcServiceManager(
        IUnaryRpcServices unaryRpcServices,
        IReceiveStreamRpcServices receiveStreamRpcServices,
        ISecrecyChannelRpcServices secrecyChannelRpcServices,
        IConnectivityService connectivityService)
    {
        _secrecyChannelRpcServices = secrecyChannelRpcServices;

        _serviceInvokers =
            new Dictionary<ServiceFlowType,
                Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>>
            {
                {
                    ServiceFlowType.SINGLE,
                    (req, token) => unaryRpcServices.InvokeRequestAsync(req, connectivityService, token)
                },
                {
                    ServiceFlowType.RECEIVE_STREAM, receiveStreamRpcServices.ProcessRequest
                }
            };
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishSecrecyChannelAsync(
        IConnectivityService connectivityService,
        SecureEnvelope envelope,
        PubKeyExchangeType? exchangeType = null,
        CancellationToken cancellationToken = default)
    {
        return await _secrecyChannelRpcServices.EstablishAppDeviceSecrecyChannelAsync(connectivityService,
            envelope,
            exchangeType,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreSecrecyChannelAsync(
        IConnectivityService connectivityService,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _secrecyChannelRpcServices.RestoreAppDeviceSecrecyChannelAsync(connectivityService,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAuthenticatedSecureChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken = default)
    {
        return await _secrecyChannelRpcServices.AuthenticatedEstablishSecureChannelAsync(connectivityService,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<RpcFlow, NetworkFailure>> InvokeServiceRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        if (_serviceInvokers.TryGetValue(request.ActionType,
                out Func<ServiceRequest, CancellationToken, Task<Result<RpcFlow, NetworkFailure>>>? invoker))
        {
            Result<RpcFlow, NetworkFailure> result = await invoker(request, token).ConfigureAwait(false);

            return result;
        }

        return Result<RpcFlow, NetworkFailure>.Err(NetworkFailure.InvalidRequestType("Unknown action type"));
    }
}
