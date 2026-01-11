using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Services.Abstractions.Network;

public interface ISecrecyChannelRpcServices
{
    Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        IConnectivityService connectivityService,
        SecureEnvelope request,
        PubKeyExchangeType? exchangeType = null,
        CancellationToken cancellationToken = default);

    Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        IConnectivityService connectivityService,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<SecureEnvelope, NetworkFailure>> AuthenticatedEstablishSecureChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        RpcRequestContext? requestContext = null,
        CancellationToken cancellationToken = default);

    Task<Result<GetServerPublicKeysResponse, NetworkFailure>> GetServerPublicKeysAsync(
        IConnectivityService connectivityService,
        CancellationToken cancellationToken = default);
}
