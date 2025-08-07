using System;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Network.Transport.Grpc.Interceptors;
using Ecliptix.Core.Settings;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Network.Transport.Resilience;

public static class GrpcClientServiceExtensions
{
    public static void AddConfiguredGrpcClients(this IServiceCollection services)
    {
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(ConfigureClient)
            .AddDefaultGrpcConfiguration(); 

        services.AddGrpcClient<MembershipServices.MembershipServicesClient>(ConfigureClient)
            .AddDefaultGrpcConfiguration();

        services.AddGrpcClient<AuthVerificationServices.AuthVerificationServicesClient>(ConfigureClient)
            .AddDefaultGrpcConfiguration();
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, GrpcClientFactoryOptions options)
    {
        DefaultSystemSettings settings = serviceProvider.GetRequiredService<IOptions<DefaultSystemSettings>>().Value;
        
        string? endpoint = settings.DataCenterConnectionString;
        
        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("gRPC DataCenterConnectionString is not configured.");
        }

        options.Address = new Uri(endpoint);
    }

    private static void AddDefaultGrpcConfiguration(this IHttpClientBuilder builder)
    {
        builder.AddPolicyHandler((sp, _) =>
            {
                INetworkEvents networkEvents = sp.GetRequiredService<INetworkEvents>();
                return RpcResiliencePolicies.CreateUnaryResiliencePolicy(networkEvents);
            })
            //  .AddInterceptor<ResilienceInterceptor>()
            //.AddInterceptor<DeadlineInterceptor>()
            .AddInterceptor<RequestMetaDataInterceptor>();
    }
}