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

public class ReceiveStreamRpcServices : IReceiveStreamRpcServices
{
    private readonly
        Dictionary<RpcServiceType, Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>>
        _serviceHandlers;

    private readonly AuthVerificationServices.AuthVerificationServicesClient _authenticationServicesClient;

    public ReceiveStreamRpcServices(
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
    {
        _authenticationServicesClient = authenticationServicesClient;
        _serviceHandlers =
            new Dictionary<RpcServiceType,
                Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>>
            {
                { RpcServiceType.InitiateVerification, InitiateVerification }
            };
    }

    public Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request,
        CancellationToken token)
    {
        if (_serviceHandlers.TryGetValue(request.RpcServiceMethod,
                out Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>? handler))
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

    private Result<RpcFlow, NetworkFailure> InitiateVerification(CipherPayload payload,
        CancellationToken token)
    {
        try
        {
            AsyncServerStreamingCall<CipherPayload> streamingCall =
                _authenticationServicesClient.InitiateVerification(payload, cancellationToken: token);

            IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> stream =
                streamingCall.ResponseStream.ReadAllAsync(token)
                    .ToObservable()
                    .Select(response =>
                    {
                        return response != null
                            ? Result<CipherPayload, NetworkFailure>.Ok(response)
                            : Result<CipherPayload, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding(NetworkServiceMessages.RpcService.ReceivedNullResponseFromStream));
                    })
                    .Catch<Result<CipherPayload, NetworkFailure>, Exception>(ex =>
                    {
                        return Observable.Return(Result<CipherPayload, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(ex.Message, ex)));
                    })
                    .ToAsyncEnumerable();

            return Result<RpcFlow, NetworkFailure>.Ok(new RpcFlow.InboundStream(stream));
        }
        catch (Exception ex)
        {
            return Result<RpcFlow, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message, ex));
        }
    }
}