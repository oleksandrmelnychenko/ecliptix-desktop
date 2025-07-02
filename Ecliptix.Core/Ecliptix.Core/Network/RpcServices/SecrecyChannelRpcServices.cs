using System.Threading.Tasks;
using Ecliptix.Core.ResilienceStrategy;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using Grpc.Core;
using Polly.Retry;

namespace Ecliptix.Core.Network.RpcServices;

public sealed class SecrecyChannelRpcServices(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient)
{
    public async Task<PubKeyExchange> EstablishAppDeviceSecrecyChannel(
        PubKeyExchange request)
    {
        return await appDeviceServiceActionsClient.EstablishAppDeviceSecrecyChannelAsync(request);
    }

    public async Task<RestoreSecrecyChannelResponse> RestoreAppDeviceSecrecyChannelAsync(
        RestoreSecrecyChannelRequest request)
    {
        AsyncRetryPolicy<RestoreSecrecyChannelResponse> policy =
            GrpcResiliencePolicies.GetRestoreSecrecyChannelRetryPolicy();
        return await policy.ExecuteAsync(async () =>
        {
            AsyncUnaryCall<RestoreSecrecyChannelResponse>? call =
                appDeviceServiceActionsClient.RestoreAppDeviceSecrecyChannelAsync(request);
            return await call.ResponseAsync;
        });
    }
}