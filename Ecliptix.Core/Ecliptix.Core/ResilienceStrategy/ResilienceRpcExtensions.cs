using System;
using Ecliptix.Core.Interceptors;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.ResilienceStrategy;

public static class ResilienceRpcExtensions
{
    public static void AddResilientGrpcClients(this IServiceCollection services,
        Action<GrpcClientFactoryOptions> configureClientOptions)
    {
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(configureClientOptions)
        .AddPolicyHandler((_, _) => RpcResiliencePolicies.GetUnauthenticatedRetryPolicy())
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
