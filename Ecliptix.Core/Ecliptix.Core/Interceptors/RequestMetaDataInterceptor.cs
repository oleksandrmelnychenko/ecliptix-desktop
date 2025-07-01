using System;
using Ecliptix.Core.Network;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Core.Interceptors;

public class RequestMetaDataInterceptor(IClientStateProvider clientStateProvider) : Interceptor
{
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Metadata headers = context.Options.Headers ?? [];
        Metadata newMetadata = GrpcMetadataHandler.GenerateMetadata(clientStateProvider.AppInstanceId.ToString(),
            clientStateProvider.DeviceId.ToString());
        foreach (Metadata.Entry entry in newMetadata) headers.Add(entry);

        CallOptions newOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> newContext = new(
            context.Method,
            context.Host,
            newOptions);

        return continuation(request, newContext);
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        Metadata headers = context.Options.Headers ?? [];
        Metadata newMetadata =
            GrpcMetadataHandler.GenerateMetadata(clientStateProvider.AppInstanceId.ToString(),
                clientStateProvider.DeviceId.ToString());
        foreach (Metadata.Entry entry in newMetadata) headers.Add(entry);

        CallOptions newOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> newContext = new(
            context.Method,
            context.Host,
            newOptions);

        return continuation(request, newContext);
    }
}