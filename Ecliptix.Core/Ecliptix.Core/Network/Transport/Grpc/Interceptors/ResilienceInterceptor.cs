using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Network.Transport.Resilience;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Polly;

namespace Ecliptix.Core.Network.Transport.Grpc.Interceptors;

public class ResilienceInterceptor(INetworkEvents networkEvents) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    )
    {
        var policy = RpcResiliencePolicies.CreateGrpcResiliencePolicy<TResponse>(networkEvents);

        return InterceptAsyncUnaryCall(request, context, continuation, policy);
    }

    private AsyncUnaryCall<TResponse> InterceptAsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        IAsyncPolicy<TResponse> policy
    )
        where TRequest : class
        where TResponse : class
    {
        var originalCall = continuation(request, context);

        var responseAsync = policy.ExecuteAsync(async () =>
        {
            var call = continuation(request, context);
            return await call.ResponseAsync;
        });

        return new AsyncUnaryCall<TResponse>(
            responseAsync,
            originalCall.ResponseHeadersAsync,
            originalCall.GetStatus,
            originalCall.GetTrailers,
            originalCall.Dispose
        );
    }
}
