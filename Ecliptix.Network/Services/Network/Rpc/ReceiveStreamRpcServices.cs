using System.Runtime.CompilerServices;
using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Network.Services.Abstractions.Network;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Transport.Common;
using Ecliptix.Protobuf.Transport.Gateway;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Network.Services.Network.Rpc;

public sealed class ReceiveStreamRpcServices : IReceiveStreamRpcServices
{
    private readonly
        Dictionary<RpcServiceType, Func<ServiceRequest, CancellationToken, Result<RpcFlow, NetworkFailure>>>
        _serviceHandlers;

    private readonly EventGateway.EventGatewayClient _gatewayClient;
    private readonly IGrpcErrorProcessor _errorProcessor;
    private readonly IGrpcCallOptionsFactory _callOptionsFactory;
    private readonly IRpcMetaDataProvider _metaDataProvider;

    public ReceiveStreamRpcServices(
        EventGateway.EventGatewayClient gatewayClient,
        IGrpcErrorProcessor errorProcessor,
        IGrpcCallOptionsFactory callOptionsFactory,
        IRpcMetaDataProvider metaDataProvider)
    {
        _gatewayClient = gatewayClient;
        _errorProcessor = errorProcessor;
        _callOptionsFactory = callOptionsFactory;
        _metaDataProvider = metaDataProvider;
        _serviceHandlers =
            new Dictionary<RpcServiceType, Func<ServiceRequest, CancellationToken, Result<RpcFlow, NetworkFailure>>>
            {
                { RpcServiceType.InitiateVerification, InitiateVerification }
            };
    }

    public Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request,
        CancellationToken token)
    {
        if (_serviceHandlers.TryGetValue(
                request.RpcServiceMethod,
                out Func<ServiceRequest, CancellationToken, Result<RpcFlow, NetworkFailure>>? handler))
        {
            try
            {
                Result<RpcFlow, NetworkFailure> result = handler(request, token);
                return Task.FromResult(result);
            }
            catch (RpcException rpcEx)
            {
                return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(_errorProcessor.Process(rpcEx)));
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(ex.Message, ex)
                ));
            }
        }

        return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType(NetworkServiceMessages.RpcService.UNSUPPORTED_SERVICE_METHOD)
        ));
    }

    private Result<RpcFlow, NetworkFailure> InitiateVerification(ServiceRequest request,
        CancellationToken token)
    {
        try
        {
            if (!GatewayRouteCatalog.TryGetRoute(request.RpcServiceMethod, out GatewayRoute? route))
            {
                return Result<RpcFlow, NetworkFailure>.Err(
                    NetworkFailure.InvalidRequestType(
                        $"Unsupported RPC service type: {request.RpcServiceMethod}"));
            }

            CallOptions callOptions = _callOptionsFactory.Create(
                RpcServiceType.InitiateVerification,
                request.RequestContext,
                token);

            EventEnvelope envelope = GatewayTransportFactory.BuildEnvelope(
                route!,
                request.Payload,
                _metaDataProvider,
                request.RequestContext);

            AsyncUnaryCall<EventEnvelope> unaryCall =
                _gatewayClient.UnaryAsync(envelope, callOptions);

            IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> stream =
                CreateUnaryStream(unaryCall, token);

            return Result<RpcFlow, NetworkFailure>.Ok(new RpcFlow.InboundStream(stream));
        }
        catch (RpcException rpcEx)
        {
            return Result<RpcFlow, NetworkFailure>.Err(_errorProcessor.Process(rpcEx));
        }
        catch (Exception ex)
        {
            return Result<RpcFlow, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private async IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> CreateUnaryStream(
        AsyncUnaryCall<EventEnvelope> unaryCall,
        [EnumeratorCancellation] CancellationToken token)
    {
        Result<SecureEnvelope, NetworkFailure> result;
        try
        {
            EventEnvelope response = await unaryCall.ResponseAsync.WaitAsync(token).ConfigureAwait(false);
            result = ToSecureEnvelopeResult(response);
        }
        catch (RpcException rpcEx) when (!token.IsCancellationRequested)
        {
            result = Result<SecureEnvelope, NetworkFailure>.Err(_errorProcessor.Process(rpcEx));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            yield break;
        }
        catch (Exception ex)
        {
            result = Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }

        yield return result;
    }

    private static Result<SecureEnvelope, NetworkFailure> ToSecureEnvelopeResult(EventEnvelope envelope)
    {
        NetworkFailure? failure = GatewayTransportFactory.MapOutcome(envelope.Metadata);
        if (failure != null)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }

        try
        {
            return Result<SecureEnvelope, NetworkFailure>.Ok(SecureEnvelope.Parser.ParseFrom(envelope.Payload));
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Failed to parse secure envelope: {ex.Message}", ex));
        }
    }
}
