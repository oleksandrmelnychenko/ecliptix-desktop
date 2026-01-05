using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Network.Network.Abstractions.Transport;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.Common;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protobuf.Transport.Gateway;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class SecrecyChannelRpcServices : ISecrecyChannelRpcServices
{
    private readonly EventGateway.EventGatewayClient _gatewayClient;
    private readonly IGrpcErrorProcessor _errorProcessor;
    private readonly IGrpcCallOptionsFactory _callOptionsFactory;
    private readonly IRpcMetaDataProvider _metaDataProvider;

    public SecrecyChannelRpcServices(
        EventGateway.EventGatewayClient gatewayClient,
        IGrpcErrorProcessor errorProcessor,
        IGrpcCallOptionsFactory callOptionsFactory,
        IRpcMetaDataProvider metaDataProvider)
    {
        _gatewayClient = gatewayClient;
        _errorProcessor = errorProcessor;
        _callOptionsFactory = callOptionsFactory;
        _metaDataProvider = metaDataProvider;
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> EstablishAppDeviceSecrecyChannelAsync(
        IConnectivityService connectivityService,
        SecureEnvelope request,
        PubKeyExchangeType? exchangeType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        PubKeyExchangeType effectiveExchangeType = exchangeType ?? PubKeyExchangeType.DataCenterEphemeralConnect;

        return await ExecuteSecureEnvelopeAsync(
            RpcServiceType.EstablishSecrecyChannel,
            connectivityService,
            request,
            effectiveExchangeType,
            cancellationToken,
            ConnectivityReason.HANDSHAKE_SUCCEEDED).ConfigureAwait(false);
    }

    public async Task<Result<RestoreChannelResponse, NetworkFailure>> RestoreAppDeviceSecrecyChannelAsync(
        IConnectivityService connectivityService,
        RestoreChannelRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteRestoreAsync(
            connectivityService,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SecureEnvelope, NetworkFailure>> AuthenticatedEstablishSecureChannelAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await ExecuteAuthenticatedEstablishAsync(
            connectivityService,
            request,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteSecureEnvelopeAsync(
        RpcServiceType serviceType,
        IConnectivityService connectivityService,
        SecureEnvelope request,
        PubKeyExchangeType exchangeType,
        CancellationToken cancellationToken,
        ConnectivityReason successReason)
    {
        try
        {
            if (!GatewayRouteCatalog.TryGetRoute(serviceType, out GatewayRoute? route))
            {
                NetworkFailure failure = NetworkFailure.InvalidRequestType(
                    $"Unsupported RPC service type: {serviceType}");
                await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), cancellationToken)
                    .ConfigureAwait(false);
                return Result<SecureEnvelope, NetworkFailure>.Err(failure);
            }

            RpcRequestContext requestContext = RpcRequestContext.CreateNew();

            EventEnvelope envelope = GatewayTransportFactory.BuildEnvelope(
                route!,
                request,
                _metaDataProvider,
                requestContext,
                exchangeType);

            CallOptions callOptions = _callOptionsFactory.Create(serviceType, requestContext, cancellationToken);
            AsyncUnaryCall<EventEnvelope> call = _gatewayClient.UnaryAsync(envelope, callOptions);

            EventEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            Log.Debug("[SECRECY-RPC] Response metadata: Status={Status}, ErrorCode={ErrorCode}",
                response.Metadata?.Outcome?.Status ?? "null",
                response.Metadata?.Outcome?.ErrorCode ?? "null");

            NetworkFailure? outcomeFailure = GatewayTransportFactory.MapOutcome(response.Metadata);
            if (outcomeFailure != null)
            {
                Log.Warning("[SECRECY-RPC] MapOutcome returned failure: {Message}", outcomeFailure.Message);
                await connectivityService.PublishAsync(
                        ConnectivityIntent.Disconnected(outcomeFailure),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<SecureEnvelope, NetworkFailure>.Err(outcomeFailure);
            }

            SecureEnvelope payload = SecureEnvelope.Parser.ParseFrom(response.Payload);

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(response.Metadata?.Security?.ConnectId, successReason),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(payload);
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
            Log.Error(ex, "[SECRECY-RPC] Unexpected exception in ExecuteSecureEnvelopeAsync");
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async Task<Result<RestoreChannelResponse, NetworkFailure>> ExecuteRestoreAsync(
        IConnectivityService connectivityService,
        RestoreChannelRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!GatewayRouteCatalog.TryGetRoute(RpcServiceType.RestoreSecrecyChannel, out GatewayRoute? route))
            {
                NetworkFailure failure = NetworkFailure.InvalidRequestType(
                    $"Unsupported RPC service type: {RpcServiceType.RestoreSecrecyChannel}");
                await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), cancellationToken)
                    .ConfigureAwait(false);
                return Result<RestoreChannelResponse, NetworkFailure>.Err(failure);
            }

            RpcRequestContext requestContext = RpcRequestContext.CreateNew();

            EventEnvelope envelope = GatewayTransportFactory.BuildEnvelope(
                route!,
                request,
                _metaDataProvider,
                requestContext,
                PubKeyExchangeType.DataCenterEphemeralConnect);

            CallOptions callOptions = _callOptionsFactory.Create(
                RpcServiceType.RestoreSecrecyChannel,
                requestContext,
                cancellationToken);

            AsyncUnaryCall<EventEnvelope> call = _gatewayClient.UnaryAsync(envelope, callOptions);
            EventEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            NetworkFailure? outcomeFailure = GatewayTransportFactory.MapOutcome(response.Metadata);
            if (outcomeFailure != null)
            {
                await connectivityService.PublishAsync(
                        ConnectivityIntent.Disconnected(outcomeFailure),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<RestoreChannelResponse, NetworkFailure>.Err(outcomeFailure);
            }

            RestoreChannelResponse parsed = RestoreChannelResponse.Parser.ParseFrom(response.Payload);

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(response.Metadata?.Security?.ConnectId, ConnectivityReason.HANDSHAKE_SUCCEEDED),
                    cancellationToken)
                .ConfigureAwait(false);

            return Result<RestoreChannelResponse, NetworkFailure>.Ok(parsed);
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
            return Result<RestoreChannelResponse, NetworkFailure>.Err(failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result<RestoreChannelResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteAuthenticatedEstablishAsync(
        IConnectivityService connectivityService,
        AuthenticatedEstablishRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!GatewayRouteCatalog.TryGetRoute(RpcServiceType.EstablishAuthenticatedSecureChannel, out GatewayRoute? route))
            {
                NetworkFailure failure = NetworkFailure.InvalidRequestType(
                    $"Unsupported RPC service type: {RpcServiceType.EstablishAuthenticatedSecureChannel}");
                await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), cancellationToken)
                    .ConfigureAwait(false);
                return Result<SecureEnvelope, NetworkFailure>.Err(failure);
            }

            RpcRequestContext requestContext = RpcRequestContext.CreateNew();

            EventEnvelope envelope = GatewayTransportFactory.BuildEnvelope(
                route!,
                request,
                _metaDataProvider,
                requestContext,
                PubKeyExchangeType.DataCenterEphemeralConnect);

            CallOptions callOptions = _callOptionsFactory.Create(
                RpcServiceType.EstablishAuthenticatedSecureChannel,
                requestContext,
                cancellationToken);

            AsyncUnaryCall<EventEnvelope> call = _gatewayClient.UnaryAsync(envelope, callOptions);

            EventEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            NetworkFailure? outcomeFailure = GatewayTransportFactory.MapOutcome(response.Metadata);
            if (outcomeFailure != null)
            {
                await connectivityService.PublishAsync(
                        ConnectivityIntent.Disconnected(outcomeFailure),
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<SecureEnvelope, NetworkFailure>.Err(outcomeFailure);
            }

            SecureEnvelope payload = SecureEnvelope.Parser.ParseFrom(response.Payload);

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(response.Metadata?.Security?.ConnectId),
                    cancellationToken)
                .ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Ok(payload);
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
                                                ErrorI18NKeys.UNAUTHENTICATED,
                                                failure.Message);

                failure = new NetworkFailure(
                    NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE,
                    userError.Message,
                    rpcEx)
                {
                    UserError = userError
                };
            }
            else if (GrpcErrorClassifier.IsMasterKeySharesNotFound(rpcEx))
            {
                failure = new NetworkFailure(
                    NetworkFailureType.MASTER_KEY_SHARES_NOT_FOUND,
                    "Server does not have master key shares - fresh handshake required",
                    rpcEx);
            }
            else if (GrpcErrorClassifier.IsMasterKeyMismatch(rpcEx))
            {
                failure = new NetworkFailure(
                    NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE,
                    "Master key mismatch - re-authentication required",
                    rpcEx);
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
}
