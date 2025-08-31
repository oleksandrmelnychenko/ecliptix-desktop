using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class SecrecyChannelRpcServices(
    DeviceService.DeviceServiceClient deviceServiceClient
) : ISecrecyChannelRpcServices
{
    public async Task<Result<PubKeyExchange, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        PubKeyExchange request,
        PubKeyExchangeType? exchangeType = null
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        
        CallOptions callOptions = new();
        if (exchangeType.HasValue)
        {
            Metadata headers = new()
            {
                { "exchange-type", exchangeType.Value.ToString() }
            };
            callOptions = callOptions.WithHeaders(headers);
        }
        
        return await ExecuteAsync<PubKeyExchange>(
            networkEvents,
            systemEvents,
            () => deviceServiceClient.EstablishSecureChannelAsync(request, callOptions)
        );
    }

    public async Task<
        Result<RestoreChannelResponse, NetworkFailure>
    > RestoreAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        RestoreChannelRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        return await ExecuteAsync<RestoreChannelResponse>(
            networkEvents,
            systemEvents,
            () => deviceServiceClient.RestoreSecureChannelAsync(request)
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
