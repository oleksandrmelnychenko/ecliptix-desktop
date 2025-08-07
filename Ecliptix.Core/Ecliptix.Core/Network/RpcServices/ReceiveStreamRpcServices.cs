using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

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

        public async Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request,
            CancellationToken token)
        {
            if (_serviceHandlers.TryGetValue(request.RcpServiceMethod,
                    out Func<CipherPayload, CancellationToken, Result<RpcFlow, NetworkFailure>>? handler))
            {
                try
                {
                    Result<RpcFlow, NetworkFailure> result = handler(request.Payload, token);
                    
                    if (result.IsOk)
                    {
                        Log.Debug("Stream service {ServiceMethod} processed successfully for req_id: {ReqId}", 
                            request.RcpServiceMethod, request.ReqId);
                    }
                    else
                    {
                        Log.Warning("Stream service {ServiceMethod} failed for req_id: {ReqId}. Error: {Error}", 
                            request.RcpServiceMethod, request.ReqId, result.UnwrapErr().Message);
                    }
                    
                    return result;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Stream service {ServiceMethod} threw exception for req_id: {ReqId}", 
                        request.RcpServiceMethod, request.ReqId);
                    return Result<RpcFlow, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding(ex.Message, ex)
                    );
                }
            }

            Log.Warning("Unsupported stream service method: {ServiceMethod} for req_id: {ReqId}", 
                request.RcpServiceMethod, request.ReqId);
            return Result<RpcFlow, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Unsupported service method")
            );
        }

        private Result<RpcFlow, NetworkFailure> InitiateVerification(CipherPayload payload,
            CancellationToken token)
        {
            try
            {
                Log.Debug("Initiating verification stream with payload size: {PayloadSize}", payload?.Cipher?.Length ?? 0);
                
                AsyncServerStreamingCall<CipherPayload> streamingCall =
                    _authenticationServicesClient.InitiateVerification(payload, cancellationToken: token);

                IAsyncEnumerable<Result<CipherPayload, NetworkFailure>> stream =
                    streamingCall.ResponseStream.ReadAllAsync(token)
                        .ToObservable()
                        .Select(response => {
                            Log.Debug("Received stream response with size: {ResponseSize}", response?.Cipher?.Length ?? 0);
                            return Result<CipherPayload, NetworkFailure>.Ok(response);
                        })
                        .Catch<Result<CipherPayload, NetworkFailure>, Exception>(ex =>
                        {
                            Log.Warning(ex, "Stream response error: {Message}", ex.Message);
                            return Observable.Return(Result<CipherPayload, NetworkFailure>.Err(
                                NetworkFailure.DataCenterNotResponding(ex.Message, ex)));
                        })
                        .ToAsyncEnumerable();

                Log.Debug("Verification stream initialized successfully");
                return Result<RpcFlow, NetworkFailure>.Ok(new RpcFlow.InboundStream(stream));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Stream initialization failed: {Message}", ex.Message);
                return Result<RpcFlow, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(ex.Message, ex));
            }
        }
    }
}