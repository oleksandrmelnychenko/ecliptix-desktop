using System;
using System.Net.Http;
using Ecliptix.Core.AppEvents.Network;
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
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(configureClientOptions)
            .AddPolicyHandler((_, _) => Policy.NoOpAsync<HttpResponseMessage>())
            .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<MembershipServices.MembershipServicesClient>(configureClientOptions)
            .AddPolicyHandler((sp, _) =>
            {
                INetworkEvents networkEvents = sp.GetRequiredService<INetworkEvents>();
                return RpcResiliencePolicies.CreateUnaryResiliencePolicy(networkEvents);
            })
            .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<AuthVerificationServices.AuthVerificationServicesClient>(configureClientOptions)
            .AddPolicyHandler((sp, _) =>
            {
                INetworkEvents networkEvents = sp.GetRequiredService<INetworkEvents>();
                return RpcResiliencePolicies.CreateUnaryResiliencePolicy(networkEvents);
            })
            .AddInterceptor<RequestMetaDataInterceptor>();
    }
}