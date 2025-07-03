using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protocol.System.Utilities;
using Grpc.Core;

namespace Ecliptix.Core.Network.RpcServices
{
    public class ReceiveStreamRpcServices
    {
        private readonly
            Dictionary<RcpServiceType, Func<CipherPayload, CancellationToken, Result<RpcFlow, EcliptixProtocolFailure>>>
            _serviceHandlers;

        private readonly AuthVerificationServices.AuthVerificationServicesClient _authenticationServicesClient;

        public ReceiveStreamRpcServices(
            AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
        {
            _authenticationServicesClient = authenticationServicesClient;
            _serviceHandlers =
                new Dictionary<RcpServiceType,
                    Func<CipherPayload, CancellationToken, Result<RpcFlow, EcliptixProtocolFailure>>>
                {
                    { RcpServiceType.InitiateVerification, InitiateVerification }
                };
        }

        public Task<Result<RpcFlow, EcliptixProtocolFailure>> ProcessRequest(ServiceRequest request,
            CancellationToken token)
        {
            if (_serviceHandlers.TryGetValue(request.RcpServiceMethod,
                    out Func<CipherPayload, CancellationToken, Result<RpcFlow, EcliptixProtocolFailure>>? handler))
            {
                return Task.FromResult(handler(request.Payload, token));
            }

            return Task.FromResult(Result<RpcFlow, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Unsupported service method")
            ));
        }

        private Result<RpcFlow, EcliptixProtocolFailure> InitiateVerification(CipherPayload payload,
            CancellationToken token)
        {
            try
            {
                AsyncServerStreamingCall<CipherPayload> streamingCall =
                    _authenticationServicesClient.InitiateVerification(payload, cancellationToken: token);

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
            catch (Exception ex)
            {
                return Result<RpcFlow, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(ex.Message, ex));
            }
        }
    }
}