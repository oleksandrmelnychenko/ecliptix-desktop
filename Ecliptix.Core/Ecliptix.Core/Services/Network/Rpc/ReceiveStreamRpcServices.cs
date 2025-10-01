using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public class ReceiveStreamRpcServices : IReceiveStreamRpcServices
{
    private readonly
        Dictionary<RpcServiceType, Func<SecureEnvelope, CancellationToken, Result<RpcFlow, NetworkFailure>>>
        _serviceHandlers;

    private readonly AuthVerificationServices.AuthVerificationServicesClient _authenticationServicesClient;

    public ReceiveStreamRpcServices(
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
    {
        _authenticationServicesClient = authenticationServicesClient;
        _serviceHandlers =
            new Dictionary<RpcServiceType,
                Func<SecureEnvelope, CancellationToken, Result<RpcFlow, NetworkFailure>>>
            {
                { RpcServiceType.InitiateVerification, InitiateVerification }
            };
    }

    public Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request,
        CancellationToken token)
    {
        if (_serviceHandlers.TryGetValue(request.RpcServiceMethod,
                out Func<SecureEnvelope, CancellationToken, Result<RpcFlow, NetworkFailure>>? handler))
        {
            try
            {
                Result<RpcFlow, NetworkFailure> result = handler(request.Payload, token);
                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(ex.Message, ex)
                ));
            }
        }

        return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType(NetworkServiceMessages.RpcService.UnsupportedServiceMethod)
        ));
    }

    private Result<RpcFlow, NetworkFailure> InitiateVerification(SecureEnvelope payload,
        CancellationToken token)
    {
        try
        {
            AsyncServerStreamingCall<SecureEnvelope> streamingCall =
                _authenticationServicesClient.InitiateVerification(payload, cancellationToken: token);

            IAsyncEnumerable<Result<SecureEnvelope, NetworkFailure>> stream =
                streamingCall.ResponseStream.ReadAllAsync(token)
                    .ToObservable()
                    .Select(response =>
                    {
                        return response != null
                            ? Result<SecureEnvelope, NetworkFailure>.Ok(response)
                            : Result<SecureEnvelope, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding(NetworkServiceMessages.RpcService.ReceivedNullResponseFromStream));
                    })
                    .Catch<Result<SecureEnvelope, NetworkFailure>, RpcException>(rpcEx =>
                    {
                        NetworkFailure failure = ClassifyRpcException(rpcEx);
                        return Observable.Return(Result<SecureEnvelope, NetworkFailure>.Err(failure));
                    })
                    .Catch<Result<SecureEnvelope, NetworkFailure>, Exception>(ex =>
                    {
                        return Observable.Return(Result<SecureEnvelope, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(ex.Message, ex)));
                    })
                    .ToAsyncEnumerable();

            return Result<RpcFlow, NetworkFailure>.Ok(new RpcFlow.InboundStream(stream));
        }
        catch (RpcException rpcEx)
        {
            return Result<RpcFlow, NetworkFailure>.Err(ClassifyRpcException(rpcEx));
        }
        catch (Exception ex)
        {
            return Result<RpcFlow, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }

    private static NetworkFailure ClassifyRpcException(RpcException rpcEx)
    {
        if (GrpcErrorClassifier.IsBusinessError(rpcEx))
        {
            return NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}");
        }

        if (GrpcErrorClassifier.IsAuthenticationError(rpcEx))
        {
            return NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}");
        }

        if (GrpcErrorClassifier.IsCancelled(rpcEx))
        {
            throw rpcEx;
        }

        if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcEx))
        {
            return NetworkFailure.ProtocolStateMismatch(rpcEx.Status.Detail ?? "Protocol state mismatch");
        }

        if (GrpcErrorClassifier.IsServerShutdown(rpcEx))
        {
            return NetworkFailure.DataCenterShutdown(rpcEx.Status.Detail ?? "Server unavailable");
        }

        if (GrpcErrorClassifier.RequiresHandshakeRecovery(rpcEx))
        {
            return NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Connection recovery needed");
        }

        if (GrpcErrorClassifier.IsTransientInfrastructure(rpcEx))
        {
            return NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Temporary failure");
        }

        return NetworkFailure.DataCenterNotResponding(rpcEx.Message);
    }
}