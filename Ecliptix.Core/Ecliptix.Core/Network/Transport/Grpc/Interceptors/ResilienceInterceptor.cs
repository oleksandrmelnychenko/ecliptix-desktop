using System.Threading.Tasks;
using Ecliptix.Core.Network.Transport.Resilience;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Polly;

namespace Ecliptix.Core.Network.Transport.Grpc.Interceptors;

public class ResilienceInterceptor : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    )
    {
        IAsyncPolicy<TResponse> policy = RpcResiliencePolicies.CreateGrpcResiliencePolicy<TResponse>();

        return InterceptAsyncUnaryCall(request, context, continuation, policy);
    }

    private static AsyncUnaryCall<TResponse> InterceptAsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation,
        IAsyncPolicy<TResponse> policy
    )
        where TRequest : class
        where TResponse : class
    {
        AsyncUnaryCall<TResponse> originalCall = continuation(request, context);

        Task<TResponse>? responseAsync = policy.ExecuteAsync(async () =>
        {
            AsyncUnaryCall<TResponse> call = continuation(request, context);
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
