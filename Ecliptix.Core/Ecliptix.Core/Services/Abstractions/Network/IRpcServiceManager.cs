using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface IRpcServiceManager
{
    Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> serviceRequest);

    Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        SecrecyKeyExchangeServiceRequest<RestoreChannelRequest, RestoreChannelResponse> serviceRequest);

    Task<Result<RpcFlow, NetworkFailure>> InvokeServiceRequestAsync(ServiceRequest request, CancellationToken token);
}