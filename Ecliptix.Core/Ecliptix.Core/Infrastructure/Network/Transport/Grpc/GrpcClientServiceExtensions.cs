using System;
using Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;
using Ecliptix.Core.Settings;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc;

public static class GrpcClientServiceExtensions
{
    public static void AddConfiguredGrpcClients(this IServiceCollection services)
    {
        services.AddGrpcClient<DeviceService.DeviceServiceClient>(ConfigureClient)
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
