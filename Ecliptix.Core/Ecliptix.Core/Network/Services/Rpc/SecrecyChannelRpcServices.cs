using System;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Core.Network.Services.Rpc;

public sealed class SecrecyChannelRpcServices(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient
) : ISecrecyChannelRpcServices
{
    public async Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
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
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
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
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        Func<AsyncUnaryCall<TResponse>> grpcCallFactory
    )
    {
        try
        {
            AsyncUnaryCall<TResponse> call = grpcCallFactory();
            TResponse response = await call.ResponseAsync;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                networkEvents.InitiateChangeState(
                    NetworkStatusChangedEvent.New(NetworkStatus.DataCenterConnected)
                );
            });

            return Result<TResponse, NetworkFailure>.Ok(response);
        }
        catch (Exception exc)
        {
            Log.Debug(exc, "Secrecy channel gRPC call failed: {Message}", exc.Message);
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.DataCenterShutdown));
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterShutdown(exc.Message)
            );
        }
    }
}
