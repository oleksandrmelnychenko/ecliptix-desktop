using System;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ResilienceStrategy;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using Grpc.Core;
using Polly;
using Polly.Retry;
using Serilog;

namespace Ecliptix.Core.Network.RpcServices;

public sealed class SecrecyChannelRpcServices(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient)
{
    public async Task<PubKeyExchange> EstablishAppDeviceSecrecyChannel(PubKeyExchange request) =>
        await ExecuteWithRetryAsync(() => appDeviceServiceActionsClient.EstablishAppDeviceSecrecyChannelAsync(request));

    public async Task<RestoreSecrecyChannelResponse> RestoreAppDeviceSecrecyChannelAsync(
        RestoreSecrecyChannelRequest request) =>
        await ExecuteWithRetryAsync(() =>
            appDeviceServiceActionsClient.RestoreAppDeviceSecrecyChannelAsync(request));

    private static async Task<TResponse> ExecuteWithRetryAsync<TResponse>(
        Func<AsyncUnaryCall<TResponse>> grpcCallFactory)
    {
        try
        {
            AsyncRetryPolicy<TResponse> policy = RpcResiliencePolicies.GetSecrecyChannelRetryPolicy<TResponse>();
            return await policy.ExecuteAsync(async () =>
            {
                AsyncUnaryCall<TResponse> call = grpcCallFactory();
                return await call.ResponseAsync;
            });
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}