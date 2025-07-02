using System;
using System.Net;
using System.Net.Http;
using Ecliptix.Core.Interceptors;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Ecliptix.Core.ResilienceStrategy;

public static class ResilienceExtensions
{
    public static IServiceCollection AddResilientGrpcClients(this IServiceCollection services,
        Action<GrpcClientFactoryOptions> configureClientOptions)
    {
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(configureClientOptions)
        .AddPolicyHandler((_, _) => GrpcResiliencePolicies.GetUnauthenticatedRetryPolicy())
        .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<MembershipServices.MembershipServicesClient>(configureClientOptions)
        .AddPolicyHandler((sp, _) => {
            ISessionManager sessionManager = sp.GetRequiredService<ISessionManager>();
            return GrpcResiliencePolicies.GetAuthenticatedPolicy(sessionManager);
        })
        .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<AuthVerificationServices.AuthVerificationServicesClient>(configureClientOptions)
        .AddPolicyHandler((sp, _) => {
            ISessionManager sessionManager = sp.GetRequiredService<ISessionManager>();
            return GrpcResiliencePolicies.GetAuthenticatedPolicy(sessionManager);
        })
        .AddInterceptor<RequestMetaDataInterceptor>();

        return services;
    }
}