using System;
using Ecliptix.Core.Network.Transport.Grpc.Interceptors;
using Ecliptix.Core.Settings;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Network.Transport;

public static class GrpcClientServiceExtensions
{
    public static void AddConfiguredGrpcClients(this IServiceCollection services)
    {
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(ConfigureClient)
            .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<MembershipServices.MembershipServicesClient>(ConfigureClient)
            .AddInterceptor<RequestMetaDataInterceptor>();

        services.AddGrpcClient<AuthVerificationServices.AuthVerificationServicesClient>(ConfigureClient)
            .AddInterceptor<RequestMetaDataInterceptor>();
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, GrpcClientFactoryOptions options)
    {
        DefaultSystemSettings settings = serviceProvider.GetRequiredService<IOptions<DefaultSystemSettings>>().Value;

        string endpoint = settings.DataCenterConnectionString;

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("gRPC DataCenterConnectionString is not configured.");
        }

        options.Address = new Uri(endpoint);
    }

}