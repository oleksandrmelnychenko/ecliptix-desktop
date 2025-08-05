using System;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.ResilienceStrategy;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Polly.Retry;

namespace Ecliptix.Core.Network.RpcServices;

public sealed class SecrecyChannelRpcServices(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient
)
{
    /// <summary>
    ///     Establishes a secrecy channel with the app device service.
    /// </summary>
    public async Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        PubKeyExchange request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteWithRetryAsync(
            networkEvents,
            systemEvents,
            () => appDeviceServiceActionsClient.EstablishAppDeviceSecrecyChannelAsync(request)
        );
    }

    /// <summary>
    ///     Restores a secrecy channel with the app device service.
    /// </summary>
    public async Task<
        Result<RestoreSecrecyChannelResponse, NetworkFailure>
    > RestoreAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        RestoreSecrecyChannelRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteWithRetryAsync(
            networkEvents,
            systemEvents,
            () => appDeviceServiceActionsClient.RestoreAppDeviceSecrecyChannelAsync(request)
        );
    }

    private static async Task<Result<TResponse, NetworkFailure>> ExecuteWithRetryAsync<TResponse>(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        Func<AsyncUnaryCall<TResponse>> grpcCallFactory
    )
    {
        try
        {
            // var policy = RpcResiliencePolicies.CreateGrpcResiliencePolicy<TResponse>(networkEvents);
            //
            // TResponse response = await policy.ExecuteAsync(async () =>
            // {
            AsyncUnaryCall<TResponse> call = grpcCallFactory();
            var response = await call.ResponseAsync;
            //});

            networkEvents.InitiateChangeState(
                NetworkStatusChangedEvent.New(NetworkStatus.DataCenterConnected)
            );

            return Result<TResponse, NetworkFailure>.Ok(response);
        }
        catch (Exception exc)
        {
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.DataCenterShutdown));
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterShutdown(exc.Message)
            );
        }
    }
}
