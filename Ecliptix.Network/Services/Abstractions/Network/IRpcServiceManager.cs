using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Services.Abstractions.Network;

public interface IRpcServiceManager
{
    Task<Result<SecureEnvelope, NetworkFailure>> EstablishSecrecyChannelAsync(
        IConnectivityService connectivityService,
        SecureEnvelope envelope,
        PubKeyExchangeType? exchangeType = null,
        CancellationToken cancellationToken = default);

    Task<Result<SecureEnvelope, NetworkFailure>> EstablishAuthenticatedSecrecyChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreSecrecyChannelAsync(
        IConnectivityService connectivityService,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<RpcFlow, NetworkFailure>> InvokeServiceRequestAsync(ServiceRequest request, CancellationToken token);

    Task<Result<GetServerPublicKeysResponse, NetworkFailure>> GetServerPublicKeysAsync(
        IConnectivityService connectivityService,
        CancellationToken cancellationToken = default);
}
