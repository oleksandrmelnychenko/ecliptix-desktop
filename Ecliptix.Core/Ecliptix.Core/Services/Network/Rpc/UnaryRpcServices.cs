using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Network.Network.Abstractions.Transport;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Transport.Common;
using Ecliptix.Protobuf.Transport.Gateway;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class UnaryRpcServices : IUnaryRpcServices
{
    private readonly EventGateway.EventGatewayClient _gatewayClient;
    private readonly IGrpcErrorProcessor _errorProcessor;
    private readonly IGrpcCallOptionsFactory _callOptionsFactory;
    private readonly IRpcMetaDataProvider _metaDataProvider;

    public UnaryRpcServices(
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

    public async Task<Result<RpcFlow, NetworkFailure>> InvokeRequestAsync(
        ServiceRequest request,
        IConnectivityService connectivityService,
        CancellationToken token)
    {
        if (!GatewayRouteCatalog.TryGetRoute(request.RpcServiceMethod, out GatewayRoute? route))
        {
            NetworkFailure failure = NetworkFailure.InvalidRequestType(
                $"Unsupported RPC service type: {request.RpcServiceMethod}");
            await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                .ConfigureAwait(false);
            return Result<RpcFlow, NetworkFailure>.Err(failure);
        }

        EventEnvelope envelope;
        try
        {
            envelope = GatewayTransportFactory.BuildEnvelope(
                route!,
                request.Payload,
                _metaDataProvider,
                request.RequestContext);
        }
        catch (Exception ex)
        {
            NetworkFailure failure = NetworkFailure.InvalidRequestType(
                $"Failed to build transport envelope: {ex.Message}", ex);
            await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                .ConfigureAwait(false);
            return Result<RpcFlow, NetworkFailure>.Err(failure);
        }

        try
        {
            CallOptions callOptions = _callOptionsFactory.Create(
                request.RpcServiceMethod,
                request.RequestContext,
                token);

            AsyncUnaryCall<EventEnvelope> call = _gatewayClient.UnaryAsync(envelope, callOptions);

            Task<Result<SecureEnvelope, NetworkFailure>> responseTask =
                HandleUnaryResponseAsync(call.ResponseAsync, connectivityService, token);

            return Result<RpcFlow, NetworkFailure>.Ok(new RpcFlow.SingleCall(responseTask));
        }
        catch (RpcException rpcEx) when (!GrpcErrorClassifier.IsCancelled(rpcEx))
        {
            NetworkFailure failure = _errorProcessor.Process(rpcEx);
            await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                .ConfigureAwait(false);
            return Result<RpcFlow, NetworkFailure>.Err(failure);
        }
        catch (RpcException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            NetworkFailure failure = NetworkFailure.DataCenterNotResponding(ex.Message, ex);
            await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                .ConfigureAwait(false);
            return Result<RpcFlow, NetworkFailure>.Err(failure);
        }
    }

    private async Task<Result<SecureEnvelope, NetworkFailure>> HandleUnaryResponseAsync(
        Task<EventEnvelope> responseTask,
        IConnectivityService connectivityService,
        CancellationToken token)
    {
        try
        {
            EventEnvelope response = await responseTask.ConfigureAwait(false);

            NetworkFailure? failure = GatewayTransportFactory.MapOutcome(response.Metadata);
            if (failure != null)
            {
                await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                    .ConfigureAwait(false);
                return Result<SecureEnvelope, NetworkFailure>.Err(failure);
            }

            SecureEnvelope payload = ParseSecureEnvelope(response);

            await connectivityService.PublishAsync(
                    ConnectivityIntent.Connected(response.Metadata?.Security?.ConnectId),
                    token)
                .ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(payload);
        }
        catch (RpcException rpcEx) when (!GrpcErrorClassifier.IsCancelled(rpcEx))
        {
            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx).ConfigureAwait(false);
            await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                .ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (RpcException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            NetworkFailure failure = NetworkFailure.DataCenterNotResponding(ex.Message, ex);
            await connectivityService.PublishAsync(ConnectivityIntent.Disconnected(failure), token)
                .ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
    }

    private static SecureEnvelope ParseSecureEnvelope(EventEnvelope response)
    {
        if (response.Payload == null || response.Payload.Length == 0)
        {
            throw new InvalidOperationException("Transport response payload was empty.");
        }

        return SecureEnvelope.Parser.ParseFrom(response.Payload);
    }
}
