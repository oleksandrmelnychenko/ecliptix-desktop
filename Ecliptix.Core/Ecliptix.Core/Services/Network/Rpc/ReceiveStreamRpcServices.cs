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
using Serilog;

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

                if (result.IsOk)
                {
                    Log.Debug("Stream service {ServiceMethod} processed successfully for req_id: {ReqId}",
                        request.RpcServiceMethod, request.ReqId);
                }
                else
                {
                    Log.Warning("Stream service {ServiceMethod} failed for req_id: {ReqId}. Error: {Error}",
                        request.RpcServiceMethod, request.ReqId, result.UnwrapErr().Message);
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Stream service {ServiceMethod} threw exception for req_id: {ReqId}",
                    request.RpcServiceMethod, request.ReqId);
                return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(ex.Message, ex)
                ));
            }
        }

        Log.Warning("Unsupported stream service method: {ServiceMethod} for req_id: {ReqId}",
            request.RpcServiceMethod, request.ReqId);
        return Task.FromResult(Result<RpcFlow, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Unsupported service method")
        ));
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
                    .Select(response =>
                    {
                        Log.Debug("Received stream response with size: {ResponseSize}", response?.Cipher?.Length ?? 0);
                        return response != null 
                            ? Result<CipherPayload, NetworkFailure>.Ok(response)
                            : Result<CipherPayload, NetworkFailure>.Err(NetworkFailure.DataCenterNotResponding("Received null response from stream"));
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