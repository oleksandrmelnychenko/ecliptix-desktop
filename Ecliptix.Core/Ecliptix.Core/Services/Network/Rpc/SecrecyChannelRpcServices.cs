using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class SecrecyChannelRpcServices(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient
) : ISecrecyChannelRpcServices
{
    public async Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        PubKeyExchange request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteAsync(
            networkEvents,
            systemEvents,
            () => appDeviceServiceActionsClient.EstablishAppDeviceSecrecyChannelAsync(request)
        );
    }

    public async Task<
        Result<RestoreSecrecyChannelResponse, NetworkFailure>
    > RestoreAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        RestoreSecrecyChannelRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteAsync(
            networkEvents,
            systemEvents,
            () => appDeviceServiceActionsClient.RestoreAppDeviceSecrecyChannelAsync(request)
        );
    }

    private static async Task<Result<TResponse, NetworkFailure>> ExecuteAsync<TResponse>(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        Func<AsyncUnaryCall<TResponse>> grpcCallFactory
    )
    {
        try
        {
            AsyncUnaryCall<TResponse> call = grpcCallFactory();
            TResponse response = await call.ResponseAsync;

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);

            return Result<TResponse, NetworkFailure>.Ok(response);
        }
        catch (Exception exc)
        {
            Log.Debug(exc, "Secrecy channel gRPC call failed: {Message}", exc.Message);
            await systemEvents.NotifySystemStateAsync(SystemState.DataCenterShutdown);
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterShutdown(exc.Message)
            );
        }
    }
}
