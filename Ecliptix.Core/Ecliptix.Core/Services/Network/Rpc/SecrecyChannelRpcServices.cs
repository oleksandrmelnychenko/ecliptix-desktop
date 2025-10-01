using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Resilience;
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

    public async Task<Result<SecureEnvelope, NetworkFailure>> AuthenticatedEstablishSecureChannelAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        AuthenticatedEstablishRequest request
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            AsyncUnaryCall<SecureEnvelope> call = deviceServiceClient.AuthenticatedEstablishSecureChannelAsync(request);
            SecureEnvelope response = await call.ResponseAsync;

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsBusinessError(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}"));
            }

            if (GrpcErrorClassifier.IsAuthenticationError(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}"));
            }

            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.ProtocolStateMismatch(rpcEx.Status.Detail ?? "Protocol state mismatch"));
            }

            if (GrpcErrorClassifier.IsServerShutdown(rpcEx))
            {
                await systemEvents.NotifySystemStateAsync(SystemState.DataCenterShutdown);
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.DataCenterShutdown(rpcEx.Status.Detail ?? "Server unavailable"));
            }

            if (GrpcErrorClassifier.RequiresHandshakeRecovery(rpcEx))
            {
                await systemEvents.NotifySystemStateAsync(SystemState.Recovering);
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Connection recovery needed"));
            }

            if (GrpcErrorClassifier.IsTransientInfrastructure(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Temporary failure"));
            }

            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(rpcEx.Message));
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
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
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsBusinessError(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}"));
            }

            if (GrpcErrorClassifier.IsAuthenticationError(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}"));
            }

            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.ProtocolStateMismatch(rpcEx.Status.Detail ?? "Protocol state mismatch"));
            }

            if (GrpcErrorClassifier.IsServerShutdown(rpcEx))
            {
                await systemEvents.NotifySystemStateAsync(SystemState.DataCenterShutdown);
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.DataCenterShutdown(rpcEx.Status.Detail ?? "Server unavailable"));
            }

            if (GrpcErrorClassifier.RequiresHandshakeRecovery(rpcEx))
            {
                await systemEvents.NotifySystemStateAsync(SystemState.Recovering);
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Connection recovery needed"));
            }

            if (GrpcErrorClassifier.IsTransientInfrastructure(rpcEx))
            {
                return Result<SecureEnvelope, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Temporary failure"));
            }

            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(rpcEx.Message));
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
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
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsBusinessError(rpcEx))
            {
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}"));
            }

            if (GrpcErrorClassifier.IsAuthenticationError(rpcEx))
            {
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}"));
            }

            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcEx))
            {
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.ProtocolStateMismatch(rpcEx.Status.Detail ?? "Protocol state mismatch"));
            }

            if (GrpcErrorClassifier.IsServerShutdown(rpcEx))
            {
                await systemEvents.NotifySystemStateAsync(SystemState.DataCenterShutdown);
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterShutdown(rpcEx.Status.Detail ?? "Server unavailable"));
            }

            if (GrpcErrorClassifier.RequiresHandshakeRecovery(rpcEx))
            {
                await systemEvents.NotifySystemStateAsync(SystemState.Recovering);
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Connection recovery needed"));
            }

            if (GrpcErrorClassifier.IsTransientInfrastructure(rpcEx))
            {
                return Result<TResponse, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Temporary failure"));
            }

            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(rpcEx.Message));
        }
        catch (Exception ex)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }
}
