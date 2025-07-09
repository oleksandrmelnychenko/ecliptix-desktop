using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Network.RpcServices
{
    public class ReceiveStreamRpcServices
    {
        private readonly
            Dictionary<RcpServiceType, Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>>
            _serviceHandlers;

        private readonly AuthVerificationServices.AuthVerificationServicesClient _authenticationServicesClient;

        public ReceiveStreamRpcServices(
            AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
        {
            _authenticationServicesClient = authenticationServicesClient;
            _serviceHandlers =
                new Dictionary<RcpServiceType,
                    Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>>
                {
                    { RcpServiceType.InitiateVerification, InitiateVerification }
                };
        }

        public Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request,
            CancellationToken token)
        {
            if (_serviceHandlers.TryGetValue(request.RcpServiceMethod,
                    out Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>? handler))
            {
                return Task.FromResult(handler(request.Payload, token));
            }

            return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Unsupported service method")
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
                        .Select(Result<CipherPayload, NetworkFailure>.Ok)
                        .Catch<Result<CipherPayload, NetworkFailure>, Exception>(ex =>
                            Observable.Return(Result<CipherPayload, NetworkFailure>.Err(
                                NetworkFailure.DataCenterNotResponding(ex.Message, ex))))
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
}