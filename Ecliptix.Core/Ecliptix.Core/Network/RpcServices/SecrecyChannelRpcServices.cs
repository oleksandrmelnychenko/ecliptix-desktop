using System.Threading.Tasks;
using Ecliptix.Core.Interceptors;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network.RpcServices;

public sealed class SecrecyChannelRpcServices(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient)
{
    public async Task<Result<PubKeyExchange, EcliptixProtocolFailure>> EstablishAppDeviceSecrecyChannel(
        PubKeyExchange request)
    {
        return await Result<PubKeyExchange, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            PubKeyExchange? response =
                await appDeviceServiceActionsClient.EstablishAppDeviceSecrecyChannelAsync(request);
            return response;
        }, err => err);
    }

    public async Task<Result<RestoreSecrecyChannelResponse, EcliptixProtocolFailure>>
        RestoreAppDeviceSecrecyChannelAsync(RestoreSecrecyChannelRequest request)
    {
        return await Result<RestoreSecrecyChannelResponse, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            RestoreSecrecyChannelResponse? response =
                await appDeviceServiceActionsClient.RestoreAppDeviceSecrecyChannelAsync(request);
            return response;
        }, err => err);
    }
}