using Ecliptix.Core.Network.Providers;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Core.Network.Interceptors;

public class RequestMetaDataInterceptor(IRpcMetaDataProvider rpcMetaDataProvider) : Interceptor
{
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Metadata headers = context.Options.Headers ?? [];
        Metadata newMetadata = GrpcMetadataHandler.GenerateMetadata(rpcMetaDataProvider.AppInstanceId.ToString(),
            rpcMetaDataProvider.DeviceId.ToString());
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
            GrpcMetadataHandler.GenerateMetadata(rpcMetaDataProvider.AppInstanceId.ToString(),
                rpcMetaDataProvider.DeviceId.ToString());
        foreach (Metadata.Entry entry in newMetadata) headers.Add(entry);

        CallOptions newOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> newContext = new(
            context.Method,
            context.Host,
            newOptions);

        return continuation(request, newContext);
    }
}