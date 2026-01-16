using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Protobuf.Protocol;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Network.Infrastructure.Network.Transport.Grpc.Interceptors;

public sealed class RequestMetaDataInterceptor(IRpcMetaDataProvider rpcMetaDataProvider) : Interceptor
{
    private string AppInstanceIdString => rpcMetaDataProvider.AppInstanceId.ToString();
    private string DeviceIdString => rpcMetaDataProvider.DeviceId.ToString();

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Metadata headers = context.Options.Headers ?? [];
        string? culture = rpcMetaDataProvider.Culture;

        if (context.Options.Headers == null || context.Options.Headers.Count == 0)
        {
            Serilog.Log.Warning(
                "[RequestMetaDataInterceptor] AsyncServerStreamingCall: Headers are null or empty! context.Options.Headers is {HeaderStatus}",
                context.Options.Headers == null ? "null" : "empty");
        }

        string? incomingExchangeType = headers.GetValue("exchange-type");
        Serilog.Log.Information(
            "[RequestMetaDataInterceptor] AsyncServerStreamingCall: Method.Name={MethodName}, Method.FullName={FullName}, HeaderCount={HeaderCount}, exchange-type={ExchangeTypeHeader}",
            context.Method.Name,
            context.Method.FullName,
            headers.Count,
            incomingExchangeType ?? "null");

        PubKeyExchangeType exchangeType = GetExchangeTypeForMethod(context.Method, headers);
        Serilog.Log.Information(
            "[RequestMetaDataInterceptor] AsyncServerStreamingCall: Resolved exchangeType={ExchangeType}",
            exchangeType);

        Metadata newMetadata = GrpcMetadataHandler.GenerateMetadata(
            AppInstanceIdString,
            DeviceIdString,
            culture,
            exchangeType,
            rpcMetaDataProvider.LocalIpAddress,
            rpcMetaDataProvider.PublicIpAddress,
            rpcMetaDataProvider.Platform);
        foreach (Metadata.Entry entry in newMetadata)
        {
            headers.Add(entry);
        }

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
        string? culture = rpcMetaDataProvider.Culture;

        string? incomingExchangeType = headers.GetValue("exchange-type");
        Serilog.Log.Information(
            "[RequestMetaDataInterceptor] AsyncUnaryCall: Method.Name={MethodName}, HeaderCount={HeaderCount}, exchange-type={ExchangeTypeHeader}",
            context.Method.Name,
            headers.Count,
            incomingExchangeType ?? "null");

        PubKeyExchangeType exchangeType = GetExchangeTypeForMethod(context.Method, headers);
        Serilog.Log.Information("[RequestMetaDataInterceptor] AsyncUnaryCall: Resolved exchangeType={ExchangeType}",
            exchangeType);

        Metadata newMetadata = GrpcMetadataHandler.GenerateMetadata(
            AppInstanceIdString,
            DeviceIdString,
            culture,
            exchangeType,
            rpcMetaDataProvider.LocalIpAddress,
            rpcMetaDataProvider.PublicIpAddress,
            rpcMetaDataProvider.Platform);
        foreach (Metadata.Entry entry in newMetadata)
        {
            headers.Add(entry);
        }

        CallOptions newOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> newContext = new(
            context.Method,
            context.Host,
            newOptions);

        return continuation(request, newContext);
    }

    private static PubKeyExchangeType GetExchangeTypeForMethod<TRequest, TResponse>(Method<TRequest, TResponse> method,
        Metadata? headers = null)
    {
        if (headers == null)
        {
            return method.Name switch
            {
                "InitiateVerification" or "ServerStream" => PubKeyExchangeType.ServerStreaming,
                _ => PubKeyExchangeType.DataCenterEphemeralConnect
            };
        }

        string? exchangeTypeHeader = headers.GetValue("exchange-type");
        if (!string.IsNullOrEmpty(exchangeTypeHeader) &&
            Enum.TryParse(exchangeTypeHeader, true, out PubKeyExchangeType headerExchangeType) &&
            Enum.IsDefined(headerExchangeType))
        {
            return headerExchangeType;
        }

        return method.Name switch
        {
            "InitiateVerification" or "ServerStream" => PubKeyExchangeType.ServerStreaming,
            _ => PubKeyExchangeType.DataCenterEphemeralConnect
        };
    }
}
