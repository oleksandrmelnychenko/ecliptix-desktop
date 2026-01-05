using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Ecliptix.Core.Desktop.Constants;
using Ecliptix.Core.Infrastructure.Network.Transport;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Network;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Network.Data.Abstractions;
using Ecliptix.Network.Network.Abstractions.Core;
using Ecliptix.Network.Network.Abstractions.Transport;
using Ecliptix.Network.Network.Core.Connectivity;
using Ecliptix.Network.Network.Core.Providers;
using Ecliptix.Network.Network.Transport;
using Ecliptix.Network.Network.Transport.Grpc.Interceptors;
using Ecliptix.Network.Security.Abstractions;
using Ecliptix.Network.Security.Crypto;
using Ecliptix.Security.Certificate.Pinning.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace Ecliptix.Core.Desktop.DI;

public static class NetworkServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(InternetConnectivityObserver.HTTP_CLIENT_NAME, client =>
        {
            InternetConnectivityObserverOptions options = InternetConnectivityObserverOptions.Default;
            client.Timeout = options.ProbeTimeout;
        });

        services.AddHttpClient<IIpGeolocationService, IpGeolocationService>()
            .SetHandlerLifetime(ApplicationConstants.Timeouts.HttpClientLifetime)
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(
                    retryCount: ApplicationConstants.Thresholds.RETRY_ATTEMPTS,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromSeconds(Math.Pow(ApplicationConstants.Thresholds.EXPONENTIAL_BACKOFF_BASE,
                            attempt))))
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(ApplicationConstants.Timeouts.HttpTimeout));

        services.AddSingleton<IInternetConnectivityObserver, InternetConnectivityObserver>();
        services.AddSingleton(new InternetConnectivityObserverOptions
        {
            PollingInterval = ApplicationConstants.Timeouts.DefaultPollingInterval,
            FailureThreshold = ApplicationConstants.Thresholds.DEFAULT_FAILURE_THRESHOLD,
            SuccessThreshold = ApplicationConstants.Thresholds.DEFAULT_SUCCESS_THRESHOLD
        });

        services.AddSingleton<IRsaChunkEncryptor, RsaChunkEncryptor>();
        services.AddSingleton<IPendingRequestManager, PendingRequestManager>();

        services.AddSingleton<NetworkProviderDependencies>(sp => new NetworkProviderDependencies(
            sp.GetRequiredService<IRpcServiceManager>(),
            sp.GetRequiredService<IApplicationSecureStorageProvider>(),
            sp.GetRequiredService<ISecureProtocolStateStorage>(),
            sp.GetRequiredService<IRpcMetaDataProvider>(),
            sp.GetRequiredService<IIdentityService>()));

        services.AddSingleton<NetworkProviderServices>(sp => new NetworkProviderServices(
            sp.GetRequiredService<IConnectivityService>(),
            sp.GetRequiredService<IRetryStrategy>(),
            sp.GetRequiredService<IPendingRequestManager>()));

        services.AddSingleton<NetworkProviderSecurity>(sp => new NetworkProviderSecurity(
            sp.GetRequiredService<ICertificatePinningServiceFactory>(),
            sp.GetRequiredService<IRsaChunkEncryptor>(),
            sp.GetRequiredService<IRetryPolicyProvider>()));

        services.AddSingleton<RetryStrategyConfiguration>(sp =>
        {
            IConfigurationSection section =
                configuration.GetSection(ApplicationConstants.Configuration.SECRECY_CHANNEL_RETRY_POLICY_SECTION);
            return CreateRetryConfiguration(section);
        });

        services.AddSingleton<IOperationTimeoutProvider, OperationTimeoutProvider>();

        services.AddSingleton<IRetryPolicyProvider>(sp =>
        {
            RetryStrategyConfiguration retryStrategyConfig = sp.GetRequiredService<RetryStrategyConfiguration>();
            return new RetryPolicyProvider(retryStrategyConfig);
        });

        services.AddSingleton<IRetryStrategy>(sp =>
        {
            RetryStrategyConfiguration retryStrategyConfig = sp.GetRequiredService<RetryStrategyConfiguration>();
            IConnectivityService connectivityService = sp.GetRequiredService<IConnectivityService>();
            IOperationTimeoutProvider timeoutProvider = sp.GetRequiredService<IOperationTimeoutProvider>();

            RetryStrategy retryStrategy = new(retryStrategyConfig, connectivityService, timeoutProvider);
            Lazy<NetworkProvider> lazyProvider = new(sp.GetRequiredService<NetworkProvider>);
            retryStrategy.SetLazyNetworkProvider(lazyProvider);
            return retryStrategy;
        });

        services.AddSingleton<IGrpcErrorProcessor, GrpcErrorProcessor>();
        services.AddSingleton<IGrpcDeadlineProvider, GrpcDeadlineProvider>();
        services.AddSingleton<IGrpcCallOptionsFactory, GrpcCallOptionsFactory>();
        services.AddSingleton<IUnaryRpcServices, UnaryRpcServices>();
        services.AddSingleton<ISecrecyChannelRpcServices, SecrecyChannelRpcServices>();
        services.AddSingleton<IReceiveStreamRpcServices, ReceiveStreamRpcServices>();
        services.AddSingleton<IRpcMetaDataProvider, RpcMetaDataProvider>();
        services.AddSingleton<RequestMetaDataInterceptor>();

        services.AddSingleton<IRpcServiceManager, RpcServiceManager>();
        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<InternetConnectivityBridge>();

        return services;
    }

    private static RetryStrategyConfiguration CreateRetryConfiguration(IConfigurationSection section)
    {
        return new RetryStrategyConfiguration
        {
            InitialRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.INITIAL_RETRY_DELAY],
                CultureInfo.InvariantCulture, out TimeSpan initialDelay)
                ? initialDelay
                : ApplicationConstants.Timeouts.DefaultInitialRetryDelay,
            MaxRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.MAX_RETRY_DELAY],
                CultureInfo.InvariantCulture, out TimeSpan maxDelay)
                ? maxDelay
                : ApplicationConstants.Timeouts.DefaultMaxRetryDelay,
            MaxRetries = int.TryParse(section[ApplicationConstants.ConfigurationKeys.MAX_RETRIES], out int maxRetries)
                ? maxRetries
                : ApplicationConstants.Thresholds.DEFAULT_MAX_RETRIES,
            PerAttemptTimeout = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.PER_ATTEMPT_TIMEOUT],
                CultureInfo.InvariantCulture, out TimeSpan perAttemptTimeout)
                ? perAttemptTimeout
                : TimeSpan.FromSeconds(30),
            UseAdaptiveRetry =
                !bool.TryParse(section[ApplicationConstants.ConfigurationKeys.USE_ADAPTIVE_RETRY], out bool adaptive) ||
                adaptive
        };
    }
}
