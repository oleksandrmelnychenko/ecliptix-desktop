using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Network.Services.Abstractions.Network;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Services.Network.Rpc;

public sealed class RpcServiceManager : IRpcServiceManager
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

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAuthenticatedSecrecyChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        RpcRequestContext? requestContext = null,
        CancellationToken cancellationToken = default)
    {
        return await _secrecyChannelRpcServices.AuthenticatedEstablishSecureChannelAsync(connectivityService,
            request,
            requestContext,
            cancellationToken).ConfigureAwait(false);
    }

    Task<Result<SecureEnvelope, NetworkFailure>> IRpcServiceManager.EstablishAuthenticatedSecrecyChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        RpcRequestContext? requestContext,
        CancellationToken cancellationToken) =>
        EstablishAuthenticatedSecrecyChannelAsync(connectivityService, request, requestContext, cancellationToken);

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

    public async Task<Result<GetServerPublicKeysResponse, NetworkFailure>> GetServerPublicKeysAsync(
        IConnectivityService connectivityService,
        CancellationToken cancellationToken = default)
    {
        return await _secrecyChannelRpcServices.GetServerPublicKeysAsync(
            connectivityService,
            cancellationToken).ConfigureAwait(false);
    }
}
