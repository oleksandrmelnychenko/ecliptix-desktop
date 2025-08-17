using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IRpcServiceManager
{
    Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        SecrecyKeyExchangeServiceRequest<PubKeyExchange, PubKeyExchange> serviceRequest);

    Task<Result<RestoreSecrecyChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        SecrecyKeyExchangeServiceRequest<RestoreSecrecyChannelRequest, RestoreSecrecyChannelResponse> serviceRequest);

    Task<Result<RpcFlow, NetworkFailure>> InvokeServiceRequestAsync(ServiceRequest request, CancellationToken token);
}