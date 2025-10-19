using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class SecrecyChannelRpcServices : ISecrecyChannelRpcServices
{
    private readonly DeviceService.DeviceServiceClient _deviceServiceClient;
    private readonly IGrpcErrorProcessor _errorProcessor;

    public SecrecyChannelRpcServices(
        DeviceService.DeviceServiceClient deviceServiceClient,
        IGrpcErrorProcessor errorProcessor)
    {
        _deviceServiceClient = deviceServiceClient;
        _errorProcessor = errorProcessor;
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        SecureEnvelope request,
        PubKeyExchangeType? exchangeType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Metadata headers = new();
        if (exchangeType.HasValue)
        {
            headers.Add("exchange-type", exchangeType.Value.ToString());
        }

        return await ExecuteSecureEnvelopeAsync(networkEvents, request, headers, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        INetworkEventService networkEvents,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteAsync(
                networkEvents,
                () => _deviceServiceClient.RestoreSecureChannelAsync(request, cancellationToken: cancellationToken))
            .ConfigureAwait(false);
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> AuthenticatedEstablishSecureChannelAsync(
        INetworkEventService networkEvents,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            AsyncUnaryCall<SecureEnvelope> call = _deviceServiceClient.AuthenticatedEstablishSecureChannelAsync(
                request,
                cancellationToken: cancellationToken);

            SecureEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected)
                .ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx, networkEvents).ConfigureAwait(false);

            if (GrpcErrorClassifier.IsIdentityKeyDerivationFailure(rpcEx))
            {
                UserFacingError userError = failure.UserError ??
                                            new UserFacingError(
                                                ErrorCode.Unauthenticated,
                                                ErrorI18nKeys.Unauthenticated,
                                                failure.Message);

                failure = new NetworkFailure(
                    NetworkFailureType.CriticalAuthenticationFailure,
                    userError.Message,
                    rpcEx)
                {
                    UserError = userError
                };
            }

            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteSecureEnvelopeAsync(
        INetworkEventService networkEvents,
        SecureEnvelope request,
        Metadata headers,
        CancellationToken cancellationToken)
    {
        try
        {
            AsyncUnaryCall<SecureEnvelope> call = _deviceServiceClient.EstablishSecureChannelAsync(
                request,
                headers,
                cancellationToken: cancellationToken);

            SecureEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected)
                .ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx, networkEvents).ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async Task<Result<TResponse, NetworkFailure>> ExecuteAsync<TResponse>(
        INetworkEventService networkEvents,
        Func<AsyncUnaryCall<TResponse>> grpcCallFactory)
    {
        try
        {
            AsyncUnaryCall<TResponse> call = grpcCallFactory();
            TResponse response = await call.ResponseAsync.ConfigureAwait(false);

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected)
                .ConfigureAwait(false);

            return Result<TResponse, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx, networkEvents).ConfigureAwait(false);
            return Result<TResponse, NetworkFailure>.Err(failure);
        }
        catch (Exception ex)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }
}
