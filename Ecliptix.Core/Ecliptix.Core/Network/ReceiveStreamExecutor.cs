using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.CipherPayload;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public class ReceiveStreamExecutor(
    AuthenticationServices.AuthenticationServicesClient authenticationServicesClient)
{
    public Result<RpcFlow, ShieldFailure> ProcessRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        switch (request.RcpServiceMethod)
        {
            case RcpServiceAction.InitiateVerification:
                return InitiateVerificationAsync(request.Payload, token);
            default:
                return Result<RpcFlow, ShieldFailure>.Err(
                    ShieldFailure.Generic("Unsupported service method")
                );
        }
    }

    private Result<RpcFlow, ShieldFailure> InitiateVerificationAsync(
        CipherPayload payload,
        CancellationToken token)
    {
        AsyncServerStreamingCall<CipherPayload> streamingCall =
            authenticationServicesClient.InitiateVerification(payload);

        IAsyncEnumerable<Result<CipherPayload, ShieldFailure>> stream =
            streamingCall.ResponseStream.ReadAllAsync(cancellationToken: token)
                .ToObservable()
                .Select(Result<CipherPayload, ShieldFailure>.Ok)
                .Catch<Result<CipherPayload, ShieldFailure>, Exception>(ex =>
                    Observable.Return(Result<CipherPayload, ShieldFailure>.Err(
                        ShieldFailure.Generic(ex.Message, ex))))
                .ToAsyncEnumerable();

        return Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.InboundStream(stream));
    }
}