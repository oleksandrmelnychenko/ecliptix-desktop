using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class ReceiveStreamRpcServices : IReceiveStreamRpcServices
{
    private readonly
        Dictionary<RpcServiceType, Func<ServiceRequest, CancellationToken, Result<RpcFlow, NetworkFailure>>>
        _serviceHandlers;

    private readonly AuthVerificationServices.AuthVerificationServicesClient _authenticationServicesClient;
    private readonly IGrpcErrorProcessor _errorProcessor;
    private readonly IGrpcCallOptionsFactory _callOptionsFactory;

    public ReceiveStreamRpcServices(
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient,
        IGrpcErrorProcessor errorProcessor,
        IGrpcCallOptionsFactory callOptionsFactory)
    {
        _authenticationServicesClient = authenticationServicesClient;
        _errorProcessor = errorProcessor;
        _callOptionsFactory = callOptionsFactory;
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
            CallOptions callOptions = _callOptionsFactory.Create(
                RpcServiceType.InitiateVerification,
                request.RequestContext,
                token);

            AsyncServerStreamingCall<SecureEnvelope> streamingCall =
                _authenticationServicesClient.InitiateVerification(request.Payload, callOptions);

            IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> stream =
                streamingCall.ResponseStream.ReadAllAsync(token)
                    .ToObservable()
                    .Select(Result<SecureEnvelope, NetworkFailure>.Ok)
                    .Catch<Result<SecureEnvelope, NetworkFailure>, RpcException>(rpcEx =>
                        Observable.Return(Result<SecureEnvelope, NetworkFailure>.Err(
                            _errorProcessor.Process(rpcEx))))
                    .Catch<Result<SecureEnvelope, NetworkFailure>, Exception>(ex =>
                        Observable.Return(Result<SecureEnvelope, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(ex.Message, ex))))
                    .ToAsyncEnumerable();

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
}
