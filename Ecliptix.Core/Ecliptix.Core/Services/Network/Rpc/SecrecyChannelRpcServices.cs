using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class SecrecyChannelRpcServices(
    DeviceService.DeviceServiceClient deviceServiceClient
) : ISecrecyChannelRpcServices
{
    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        SecureEnvelope request,
        PubKeyExchangeType? exchangeType = null
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        Metadata headers = new();
        if (exchangeType.HasValue)
        {
            headers.Add("exchange-type", exchangeType.Value.ToString());
        }

        return await ExecuteSecureEnvelopeAsync(
            networkEvents,
            systemEvents,
            request,
            headers
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

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteSecureEnvelopeAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        SecureEnvelope request,
        Metadata headers
    )
    {
        try
        {
            AsyncUnaryCall<SecureEnvelope> call = deviceServiceClient.EstablishSecureChannelAsync(request, headers);
            SecureEnvelope response = await call.ResponseAsync;

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (Exception exc)
        {
            Log.Debug(exc, "Secrecy channel gRPC call failed: {Message}", exc.Message);
            await systemEvents.NotifySystemStateAsync(SystemState.DataCenterShutdown);
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterShutdown(exc.Message)
            );
        }
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
