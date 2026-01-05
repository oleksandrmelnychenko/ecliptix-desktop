using System;
using Ecliptix.Core.Settings;
using Ecliptix.Network.Infrastructure.Network.Transport.Grpc.Interceptors;
using Ecliptix.Protobuf.Transport.Gateway;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc;

public static class GrpcClientServiceExtensions
{
    public static void AddConfiguredGrpcClients(this IServiceCollection services)
    {
        services.AddGrpcClient<EventGateway.EventGatewayClient>(ConfigureClient)
            .AddInterceptor<RequestMetaDataInterceptor>();

    }

    private static void ConfigureClient(IServiceProvider serviceProvider, GrpcClientFactoryOptions options)
    {
        DefaultSystemSettings settings = serviceProvider.GetRequiredService<IOptions<DefaultSystemSettings>>().Value;

        string endpoint = settings.DataCenterConnectionString;

        if (string.IsNullOrEmpty(endpoint))
        {
            throw new InvalidOperationException("gRPC DATA_CENTER_CONNECTION_STRING is not configured.");
        }

        options.Address = new Uri(endpoint);
    }

}
