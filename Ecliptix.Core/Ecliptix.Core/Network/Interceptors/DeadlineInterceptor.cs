using System;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Core.Network.Interceptors;

public class DeadlineInterceptor(int defaultTimeoutSeconds = 20) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation
    )
    {
        var options = context.Options;

        if (options.Deadline == null)
        {
            options = options.WithDeadline(DateTime.UtcNow.AddSeconds(defaultTimeoutSeconds));
        }

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            options
        );

        return continuation(request, newContext);
    }
}
