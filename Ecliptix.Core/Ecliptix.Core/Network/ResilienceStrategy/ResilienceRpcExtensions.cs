using System;
using System.Net.Http;
using Ecliptix.Core.Network.Interceptors;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Ecliptix.Core.Network.ResilienceStrategy;

public static class ResilienceRpcExtensions
{
    public static void AddResilientGrpcClients(this IServiceCollection services,
        Action<GrpcClientFactoryOptions> configureClientOptions)
    {
        // services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(configureClientOptions)
        // .AddPolicyHandler((_, _) => RpcResiliencePolicies.GetUnauthenticatedRetryPolicy())
        // .AddInterceptor<RequestMetaDataInterceptor>();

        // NEW ADDED MODIFIED 2025-07-07 16:12 by Vitalik Koliesnikov
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(configureClientOptions)
        .AddPolicyHandler((_, _) => Policy.NoOpAsync<HttpResponseMessage>())
        .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<MembershipServices.MembershipServicesClient>(configureClientOptions)
        .AddPolicyHandler((sp, _) => {
            INetworkProvider networkProvider = sp.GetRequiredService<INetworkProvider>();
            return RpcResiliencePolicies.GetAuthenticatedPolicy(networkProvider);
        })
        .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<AuthVerificationServices.AuthVerificationServicesClient>(configureClientOptions)
        .AddPolicyHandler((sp, _) => {
            INetworkProvider networkProvider = sp.GetRequiredService<INetworkProvider>();
            return RpcResiliencePolicies.GetAuthenticatedPolicy(networkProvider);
        })
        .AddInterceptor<RequestMetaDataInterceptor>();
    }
}
