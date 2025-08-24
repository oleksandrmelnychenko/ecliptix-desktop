using System;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Security.Pinning.Configuration;
using Ecliptix.Security.Pinning.Encryption;
using Ecliptix.Security.Pinning.Keys;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Security.Pinning.Interceptors;

public sealed class SecureHandshakeInterceptor : Interceptor
{
    private readonly IApplicationLayerEncryption? _encryption;
    private readonly IKeyProvider? _keyProvider;
    private readonly ApplicationSecurityOptions _options;

    public SecureHandshakeInterceptor(
        IApplicationLayerEncryption? encryption = null,
        IKeyProvider? keyProvider = null,
        ApplicationSecurityOptions? options = null)
    {
        _encryption = encryption;
        _keyProvider = keyProvider;
        _options = options ?? new ApplicationSecurityOptions();
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        if (!_options.EnableApplicationLayerSecurity || 
            _encryption == null || 
            _keyProvider == null ||
            !ShouldSecureMessage(context.Method.Name))
        {
            return continuation(request, context);
        }

        return SecureAsyncUnaryCall(request, context, continuation);
    }

    private AsyncUnaryCall<TResponse> SecureAsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        async Task<TResponse> SecureCall()
        {
            try
            {
                if (request is IMessage protoMessage)
                {
                    byte[] serializedPayload = protoMessage.ToByteArray();
                    string serverPublicKey = await _keyProvider!.GetServerPublicKeyAsync();
                    
                    SecuredMessage securedMessage = await _encryption!.EncryptMessageAsync(
                        serializedPayload, 
                        serverPublicKey);

                    Metadata securityHeaders = CreateSecurityHeaders(securedMessage);
                    Metadata newMetadata = context.Options.Headers ?? new Metadata();
                    foreach (Metadata.Entry header in securityHeaders)
                    {
                        newMetadata.Add(header);
                    }

                    CallOptions newOptions = context.Options.WithHeaders(newMetadata);
                    ClientInterceptorContext<TRequest, TResponse> newContext = new(
                        context.Method, context.Host, newOptions);

                    return await continuation(request, newContext).ResponseAsync;
                }
                
                return await continuation(request, context).ResponseAsync;
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Internal, 
                    $"Application layer encryption failed: {ex.Message}"));
            }
        }

        return new AsyncUnaryCall<TResponse>(
            SecureCall(),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });
    }

    private bool ShouldSecureMessage(string methodName)
    {
        if (_options.SecureAllMessages) return true;
        if (!_options.SecureFirstMessageOnly) return false;
        
        return _options.FirstRequestMethods.Any(method => 
            methodName.EndsWith(method, StringComparison.OrdinalIgnoreCase));
    }

    private static Metadata CreateSecurityHeaders(SecuredMessage securedMessage)
    {
        return new Metadata
        {
            { "x-encryption-algorithm", securedMessage.Algorithm.ToString() },
            { "x-signing-algorithm", securedMessage.SigningAlgorithm.ToString() },
            { "x-encrypted-key", Convert.ToBase64String(securedMessage.EncryptedAesKey) },
            { "x-iv", Convert.ToBase64String(securedMessage.IV) },
            { "x-signature", Convert.ToBase64String(securedMessage.Signature) },
            { "x-encrypted-payload", Convert.ToBase64String(securedMessage.EncryptedPayload) }
        };
    }
}