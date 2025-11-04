using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class SecrecyChannelRpcServices : ISecrecyChannelRpcServices
{
    private readonly DeviceService.DeviceServiceClient _deviceServiceClient;
    private readonly IGrpcErrorProcessor _errorProcessor;
    private readonly IGrpcCallOptionsFactory _callOptionsFactory;

    public SecrecyChannelRpcServices(
        DeviceService.DeviceServiceClient deviceServiceClient,
        IGrpcErrorProcessor errorProcessor,
        IGrpcCallOptionsFactory callOptionsFactory)
    {
        _deviceServiceClient = deviceServiceClient;
        _errorProcessor = errorProcessor;
        _callOptionsFactory = callOptionsFactory;
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        IConnectivityService connectivityService,
        SecureEnvelope request,
        PubKeyExchangeType? EXCHANGE_TYPE = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Metadata headers = new();
        if (EXCHANGE_TYPE.HasValue)
        {
            headers.Add("exchange-type", EXCHANGE_TYPE.Value.ToString());
        }

        return await ExecuteSecureEnvelopeAsync(
                RpcServiceType.EstablishSecrecyChannel,
                connectivityService,
                request,
                headers,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        IConnectivityService connectivityService,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteAsync(
                RpcServiceType.RestoreSecrecyChannel,
                connectivityService,
                options => _deviceServiceClient.RestoreSecureChannelAsync(request, options),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> AuthenticatedEstablishSecureChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            CallOptions callOptions = _callOptionsFactory.Create(
                RpcServiceType.EstablishAuthenticatedSecureChannel,
                requestContext: null,
                cancellationToken);
            AsyncUnaryCall<SecureEnvelope> call = _deviceServiceClient.AuthenticatedEstablishSecureChannelAsync(
                request,
                callOptions);

            SecureEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            await connectivityService.PublishAsync(ConnectivityIntent.Connected()).ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx).ConfigureAwait(false);

            if (GrpcErrorClassifier.IsIdentityKeyDerivationFailure(rpcEx))
            {
                UserFacingError userError = failure.UserError ??
                                            new UserFacingError(
                                                ErrorCode.UNAUTHENTICATED,
                                                ErrorI18nKeys.UNAUTHENTICATED,
                                                failure.Message);

                failure = new NetworkFailure(
                    NetworkFailureType.CriticalAuthenticationFailure,
                    userError.Message,
                    rpcEx)
                {
                    UserError = userError
                };
            }

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Disconnected(failure))
                .ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteSecureEnvelopeAsync(
        RpcServiceType serviceType,
        IConnectivityService connectivityService,
        SecureEnvelope request,
        Metadata headers,
        CancellationToken cancellationToken)
    {
        try
        {
            CallOptions callOptions = _callOptionsFactory.Create(serviceType, null, cancellationToken, headers);
            AsyncUnaryCall<SecureEnvelope> call = _deviceServiceClient.EstablishSecureChannelAsync(
                request,
                callOptions);

            SecureEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(null, ConnectivityReason.HandshakeSucceeded))
                .ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx).ConfigureAwait(false);
            await connectivityService.PublishAsync(
                    ConnectivityIntent.Disconnected(failure))
                .ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async Task<Result<TResponse, NetworkFailure>> ExecuteAsync<TResponse>(
        RpcServiceType serviceType,
        IConnectivityService connectivityService,
        Func<CallOptions, AsyncUnaryCall<TResponse>> grpcCallFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            CallOptions callOptions = _callOptionsFactory.Create(serviceType, null, cancellationToken);
            AsyncUnaryCall<TResponse> call = grpcCallFactory(callOptions);
            TResponse response = await call.ResponseAsync.ConfigureAwait(false);

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(null, ConnectivityReason.HandshakeSucceeded))
                .ConfigureAwait(false);

            return Result<TResponse, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            if (GrpcErrorClassifier.IsCancelled(rpcEx))
            {
                throw;
            }

            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx).ConfigureAwait(false);
            await connectivityService.PublishAsync(
                    ConnectivityIntent.Disconnected(failure))
                .ConfigureAwait(false);
            return Result<TResponse, NetworkFailure>.Err(failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }
}
