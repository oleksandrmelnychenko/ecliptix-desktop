using System;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Protobuf.Protocol;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;

public class RequestMetaDataInterceptor(IRpcMetaDataProvider rpcMetaDataProvider) : Interceptor
{
    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        Metadata headers = context.Options.Headers ?? [];
        string? culture = rpcMetaDataProvider.Culture;
        Serilog.Log.Information("RequestMetaDataInterceptor: Using culture '{Culture}' for gRPC metadata", culture);
        
        PubKeyExchangeType exchangeType = GetExchangeTypeForMethod(context.Method, headers);
        Serilog.Log.Debug("RequestMetaDataInterceptor: Using exchange type '{ExchangeType}' for method '{Method}' (streaming)", 
            exchangeType, context.Method.Name);
        
        Metadata newMetadata = GrpcMetadataHandler.GenerateMetadata(rpcMetaDataProvider.AppInstanceId.ToString(),
            rpcMetaDataProvider.DeviceId.ToString(), culture, exchangeType);
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
        string? culture = rpcMetaDataProvider.Culture;
        Serilog.Log.Information("RequestMetaDataInterceptor: Using culture '{Culture}' for gRPC metadata", culture);
        
        PubKeyExchangeType exchangeType = GetExchangeTypeForMethod(context.Method, headers);
        Serilog.Log.Debug("RequestMetaDataInterceptor: Using exchange type '{ExchangeType}' for method '{Method}' (unary)", 
            exchangeType, context.Method.Name);
        
        Metadata newMetadata =
            GrpcMetadataHandler.GenerateMetadata(rpcMetaDataProvider.AppInstanceId.ToString(),
                rpcMetaDataProvider.DeviceId.ToString(), culture, exchangeType);
        foreach (Metadata.Entry entry in newMetadata) headers.Add(entry);

        CallOptions newOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> newContext = new(
            context.Method,
            context.Host,
            newOptions);

        return continuation(request, newContext);
    }
    
    private static PubKeyExchangeType GetExchangeTypeForMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, Metadata? headers = null)
    {
        // First, check if exchange type is explicitly provided in headers (for EstablishSecureChannel)
        if (headers != null)
        {
            string? exchangeTypeHeader = headers.GetValue("exchange-type");
            if (!string.IsNullOrEmpty(exchangeTypeHeader) && 
                Enum.TryParse<PubKeyExchangeType>(exchangeTypeHeader, true, out PubKeyExchangeType headerExchangeType) &&
                Enum.IsDefined(typeof(PubKeyExchangeType), headerExchangeType))
            {
                return headerExchangeType;
            }
        }
        
        // Streaming methods that should use SERVER_STREAMING exchange type
        return method.Name switch
        {
            "InitiateVerification" => PubKeyExchangeType.ServerStreaming,
            _ => PubKeyExchangeType.DataCenterEphemeralConnect
        };
    }
}