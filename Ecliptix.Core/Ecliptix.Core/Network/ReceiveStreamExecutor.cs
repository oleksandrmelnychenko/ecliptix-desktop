using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public class ReceiveStreamExecutor(
    AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
{
    public Result<RpcFlow, EcliptixProtocolFailure> ProcessRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        switch (request.RcpServiceMethod)
        {
            case RcpServiceAction.InitiateVerification:
                return InitiateVerificationAsync(request.Payload, token);
            default:
                return Result<RpcFlow, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Unsupported service method")
                );
        }
    }

    private Result<RpcFlow, EcliptixProtocolFailure> InitiateVerificationAsync(
        CipherPayload payload,
        CancellationToken token)
    {
        AsyncServerStreamingCall<CipherPayload> streamingCall =
            authenticationServicesClient.InitiateVerification(payload);

        IAsyncEnumerable<Result<CipherPayload, EcliptixProtocolFailure>> stream =
            streamingCall.ResponseStream.ReadAllAsync(token)
                .ToObservable()
                .Select(Result<CipherPayload, EcliptixProtocolFailure>.Ok)
                .Catch<Result<CipherPayload, EcliptixProtocolFailure>, Exception>(ex =>
                    Observable.Return(Result<CipherPayload, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(ex.Message, ex))))
                .ToAsyncEnumerable();

        return Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.InboundStream(stream));
    }
}