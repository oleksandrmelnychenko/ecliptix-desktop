using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using DotNetEnv;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Desktop.Constants;
using Ecliptix.Core.Features.Authentication;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Main;
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Data.SecureStorage;
using Ecliptix.Core.Infrastructure.Data.SecureStorage.Configuration;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Connectivity;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Network.Transport;
using Ecliptix.Core.Infrastructure.Network.Transport.Grpc;
using Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Crypto;
using Ecliptix.Core.Infrastructure.Security.KeySplitting;
using Ecliptix.Core.Infrastructure.Security.Platform;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Services.Network;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Services.Security;
using Ecliptix.Core.Settings;
using Ecliptix.Security.Certificate.Pinning.Services;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Serilog;
using Serilog.Core;
using Serilog.Settings.Configuration;
using Splat.Microsoft.Extensions.DependencyInjection;
using IViewLocator = Ecliptix.Core.Core.Abstractions.IViewLocator;

namespace Ecliptix.Core.Desktop;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        string mutexName =
            string.Format(ApplicationConstants.ApplicationSettings.MutexNameFormat, Environment.UserName);
        using Mutex mutex = new(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            return;
        }

        IConfiguration configuration = BuildConfiguration();
        Env.Load();
        Log.Logger = ConfigureSerilog(configuration);

        try
        {
            Log.Information(ApplicationConstants.Logging.StartupMessage);
            IServiceCollection services = ConfigureServices(configuration);

            services.UseMicrosoftDependencyResolver();

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ReactiveUI.IViewLocator reactiveViewLocator = serviceProvider.GetRequiredService<ReactiveUI.IViewLocator>();
            Splat.Locator.CurrentMutable.Register(() => reactiveViewLocator, typeof(ReactiveUI.IViewLocator));

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, ApplicationConstants.Logging.FatalErrorMessage);
            if (configuration[ApplicationConstants.ApplicationSettings.EnvironmentKey] !=
                ApplicationConstants.ApplicationSettings.DevelopmentEnvironment)
            {
                Environment.Exit(ApplicationConstants.ExitCodes.FatalError);
            }
        }
        finally
        {
            Log.Information(ApplicationConstants.Logging.ShutdownMessage);
            await Log.CloseAndFlushAsync();
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        string? environment = Env.GetString(ApplicationConstants.ApplicationSettings.DotNetEnvironmentKey);
#if DEBUG
        environment ??= ApplicationConstants.ApplicationSettings.DevelopmentEnvironment;
#else
        environment ??= ApplicationConstants.ApplicationSettings.ProductionEnvironment;
#endif

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(ApplicationConstants.Configuration.AppSettingsFile, optional: false, reloadOnChange: true)
            .AddJsonFile(string.Format(ApplicationConstants.Configuration.EnvironmentAppSettingsPattern, environment),
                optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static Logger ConfigureSerilog(IConfiguration configuration)
    {
        try
        {
            LoggerConfiguration loggerConfig = new();

            IConfigurationSection serilogSection =
                configuration.GetSection(ApplicationConstants.Configuration.SerilogSection);

            string minLevel = serilogSection[ApplicationConstants.Configuration.MinimumLevelDefaultKey] ??
                              ApplicationConstants.LogLevels.Information;
            loggerConfig = minLevel switch
            {
                ApplicationConstants.LogLevels.Debug => loggerConfig.MinimumLevel.Debug(),
                ApplicationConstants.LogLevels.Information => loggerConfig.MinimumLevel.Information(),
                ApplicationConstants.LogLevels.Warning => loggerConfig.MinimumLevel.Warning(),
                ApplicationConstants.LogLevels.Error => loggerConfig.MinimumLevel.Error(),
                ApplicationConstants.LogLevels.Fatal => loggerConfig.MinimumLevel.Fatal(),
                _ => loggerConfig.MinimumLevel.Information()
            };

            loggerConfig = loggerConfig.WriteTo.Console();

            string logPath = Path.Combine(ApplicationConstants.Storage.LogsDirectory,
                ApplicationConstants.Storage.LogFilePattern);
            loggerConfig = loggerConfig.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);

            return loggerConfig.CreateLogger();
        }
        catch (Exception)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(ApplicationConstants.Storage.LogsDirectory,
                        ApplicationConstants.Storage.LogFilePattern), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
    }

    private static IServiceCollection ConfigureServices(IConfiguration configuration)
    {
        ServiceCollection services = new();

        ConfigureCoreServices(services, configuration);
        ConfigureNetworkServices(services);
        ConfigureSecurityServices(services, configuration);
        ConfigureMessagingServices(services);
        ConfigureAuthenticationServices(services);
        ConfigureGrpc(services);
        ConfigureModules(services);

        return services;
    }

    private static string GetSectionValue(IConfigurationSection section, string key, string defaultValue = "")
    {
        return section[key] ?? defaultValue;
    }

    private static void ConfigureCoreServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationConstants.ApplicationSettings.ApplicationName)
            .PersistKeysToFileSystem(
                new DirectoryInfo(ResolvePath(ApplicationConstants.Storage.DataProtectionKeysPath))
            )
            .SetDefaultKeyLifetime(ApplicationConstants.Timeouts.DefaultKeyLifetime);

        services.AddSingleton(configuration);
        services.AddSingleton<IScheduler>(AvaloniaScheduler.Instance);
    }

    private static void ConfigureNetworkServices(IServiceCollection services)
    {
        services.AddHttpClient(InternetConnectivityObserver.HttpClientName, client =>
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
                    retryCount: ApplicationConstants.Thresholds.RetryAttempts,
                    sleepDurationProvider: attempt =>
                        TimeSpan.FromSeconds(Math.Pow(ApplicationConstants.Thresholds.ExponentialBackoffBase,
                            attempt))))
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(ApplicationConstants.Timeouts.HttpTimeout));

        services.AddSingleton<IInternetConnectivityObserver, InternetConnectivityObserver>();
        services.AddSingleton(new InternetConnectivityObserverOptions
        {
            PollingInterval = ApplicationConstants.Timeouts.DefaultPollingInterval,
            FailureThreshold = ApplicationConstants.Thresholds.DefaultFailureThreshold,
            SuccessThreshold = ApplicationConstants.Thresholds.DefaultSuccessThreshold
        });

        services.AddSingleton<IRsaChunkEncryptor, RsaChunkEncryptor>();
        services.AddSingleton<IPendingRequestManager, PendingRequestManager>();

        services.AddSingleton<NetworkProviderDependencies>(sp => new NetworkProviderDependencies(
            sp.GetRequiredService<IRpcServiceManager>(),
            sp.GetRequiredService<IApplicationSecureStorageProvider>(),
            sp.GetRequiredService<ISecureProtocolStateStorage>(),
            sp.GetRequiredService<IRpcMetaDataProvider>()));

        services.AddSingleton<NetworkProviderServices>(sp => new NetworkProviderServices(
            sp.GetRequiredService<IConnectivityService>(),
            sp.GetRequiredService<IRetryStrategy>(),
            sp.GetRequiredService<IPendingRequestManager>()));

        services.AddSingleton<NetworkProviderSecurity>(sp => new NetworkProviderSecurity(
            sp.GetRequiredService<ICertificatePinningServiceFactory>(),
            sp.GetRequiredService<IRsaChunkEncryptor>(),
            sp.GetRequiredService<IRetryPolicyProvider>()));

        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<InternetConnectivityBridge>();
    }

    private static void ConfigureSecurityServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOptions<DefaultSystemSettings>>(_ =>
        {
            IConfigurationSection section =
                configuration.GetSection(ApplicationConstants.Configuration.DefaultAppSettingsSection);
            DefaultSystemSettings settings = new()
            {
                DefaultTheme = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DefaultTheme),
                Environment = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.Environment,
                    ApplicationConstants.ApplicationSettings.ProductionEnvironment),
                DataCenterConnectionString = GetSectionValue(section,
                    ApplicationConstants.ConfigurationKeys.DataCenterConnectionString),
                CountryCodeApi = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.CountryCodeApi),
                DomainName = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DomainName),
                Culture = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.Culture),
                PrivacyPolicyUrl = GetSectionValue(section, "PrivacyPolicyUrl"),
                TermsOfServiceUrl = GetSectionValue(section, "TermsOfServiceUrl"),
                SupportUrl = GetSectionValue(section, "SupportUrl")
            };
            return Options.Create(settings);
        });

        services.AddSingleton<IOptions<SecureStoreOptions>>(_ =>
        {
            IConfigurationSection section =
                configuration.GetSection(ApplicationConstants.Configuration.SecureStoreOptionsSection);
            SecureStoreOptions options = new()
            {
                EncryptedStatePath = ResolvePath(
                    GetSectionValue(section, ApplicationConstants.ConfigurationKeys.EncryptedStatePath,
                        ApplicationConstants.Storage.DefaultStatePath)
                )
            };
            return Options.Create(options);
        });

        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DefaultSystemSettings>>().Value);

        services.AddSingleton<ILogger<ApplicationSecureStorageProvider>>(sp =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<ApplicationSecureStorageProvider>()
        );
        services.AddSingleton<IApplicationSecureStorageProvider, ApplicationSecureStorageProvider>();

        services.AddSingleton<IPlatformSecurityProvider>(_ =>
        {
            string appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ApplicationConstants.Storage.EcliptixDirectoryName);
            return new CrossPlatformSecurityProvider(appDataPath);
        });

        services.AddSingleton<ISecureProtocolStateStorage>(sp =>
        {
            IPlatformSecurityProvider platformProvider = sp.GetRequiredService<IPlatformSecurityProvider>();
            IConfiguration config = sp.GetRequiredService<IConfiguration>();

            string storageDirectory =
                config[
                    ApplicationConstants.Configuration.SecureStorageSection +
                    ApplicationConstants.Configuration.PathSeparator + ApplicationConstants.ConfigurationKeys.StatePath]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ApplicationConstants.Storage.EcliptixDirectoryName);

            byte[] deviceId = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);

            return new SecureProtocolStateStorage(platformProvider, storageDirectory, deviceId);
        });

        services.AddSingleton<ICertificatePinningServiceFactory, CertificatePinningServiceFactory>();
        services.AddSingleton<IServerPublicKeyProvider, ServerPublicKeyProvider>();
    }

    private static void ConfigureMessagingServices(IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();
        services.AddSingleton<IBottomSheetService, BottomSheetService>();
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddTransient<ILogoutService, LogoutService>();

        services.AddSingleton<IApplicationStateManager, ApplicationStateManager>();
        services.AddSingleton<IStateCleanupService, StateCleanupService>();
        services.AddSingleton<IApplicationRouter, ApplicationRouter>();
        services.AddTransient<ApplicationStartup>();
    }

    private static void ConfigureAuthenticationServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthenticationService, OpaqueAuthenticationService>();
        services.AddSingleton<IOpaqueRegistrationService, OpaqueRegistrationService>();
        services.AddSingleton<ISecureKeyRecoveryService, SecureKeyRecoveryService>();
        services.AddSingleton<IIdentityService, IdentityService>();

        services.AddSingleton<IHardenedKeyDerivation, HardenedKeyDerivation>();

        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();
        services.AddSingleton<IRpcServiceManager, RpcServiceManager>();

        services.AddSingleton<RetryStrategyConfiguration>(sp =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            IConfigurationSection section =
                config.GetSection(ApplicationConstants.Configuration.SecrecyChannelRetryPolicySection);
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
    }

    private static RetryStrategyConfiguration CreateRetryConfiguration(IConfigurationSection section)
    {
        return new RetryStrategyConfiguration
        {
            InitialRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.InitialRetryDelay],
                out TimeSpan initialDelay)
                ? initialDelay
                : ApplicationConstants.Timeouts.DefaultInitialRetryDelay,
            MaxRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.MaxRetryDelay],
                out TimeSpan maxDelay)
                ? maxDelay
                : ApplicationConstants.Timeouts.DefaultMaxRetryDelay,
            MaxRetries = int.TryParse(section[ApplicationConstants.ConfigurationKeys.MaxRetries], out int maxRetries)
                ? maxRetries
                : ApplicationConstants.Thresholds.DefaultMaxRetries,
            PerAttemptTimeout = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.PerAttemptTimeout],
                out TimeSpan perAttemptTimeout)
                ? perAttemptTimeout
                : TimeSpan.FromSeconds(30),
            UseAdaptiveRetry =
                !bool.TryParse(section[ApplicationConstants.ConfigurationKeys.UseAdaptiveRetry], out bool adaptive) ||
                adaptive
        };
    }

    private static void ConfigureGrpc(IServiceCollection services)
    {
        services.AddSingleton((Action<GrpcClientFactoryOptions>)ConfigureClientOptions);
        services.AddConfiguredGrpcClients();
        return;

        void ConfigureClientOptions(GrpcClientFactoryOptions options)
        {
            DefaultSystemSettings settings = services.BuildServiceProvider()
                .GetRequiredService<DefaultSystemSettings>();
            string endpoint =
                settings.Environment.Equals(ApplicationConstants.ApplicationSettings.DevelopmentEnvironment,
                    StringComparison.OrdinalIgnoreCase)
                    ? settings.DataCenterConnectionString
                    : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException(ApplicationConstants.Logging.GrpcEndpointErrorMessage);
            }

            options.Address = new Uri(endpoint);
        }
    }

    private static void ConfigureModules(IServiceCollection services)
    {
        services.AddSingleton<ModuleResourceManager>();

        services.AddSingleton<IModuleMessageBus, ModuleMessageBus>();
        services.AddSingleton<IModuleViewFactory, ModuleViewFactory>();

        services.AddSingleton<IViewLocator, ViewLocator>();
        services.AddSingleton<ReactiveUiViewLocatorAdapter>();

        services.AddSingleton<ReactiveUI.IViewLocator>(provider =>
            provider.GetRequiredService<ReactiveUiViewLocatorAdapter>());

        ModuleCatalog catalog = new();
        catalog.AddModule<AuthenticationModule>();
        catalog.AddModule<MainModule>();

        services.AddSingleton<IModuleCatalog>(catalog);
        services.AddSingleton(catalog);

        services.AddSingleton<IModuleManager, ModuleManager>();

        services.AddTransient<LanguageSelectorViewModel>();
        services.AddSingleton<BottomSheetViewModel>();
        services.AddSingleton<ConnectivityNotificationViewModel>();
        services.AddSingleton<Ecliptix.Core.ViewModels.Core.MainWindowViewModel>();
        services.AddTransient<SplashWindowViewModel>();
        services.AddTransient<AuthenticationViewModel>(sp => new AuthenticationViewModel(
            new AuthenticationViewModelDependencies
            {
                ConnectivityService = sp.GetRequiredService<IConnectivityService>(),
                NetworkProvider = sp.GetRequiredService<NetworkProvider>(),
                LocalizationService = sp.GetRequiredService<ILocalizationService>(),
                StorageProvider = sp.GetRequiredService<IApplicationSecureStorageProvider>(),
                AuthenticationService = sp.GetRequiredService<IAuthenticationService>(),
                RegistrationService = sp.GetRequiredService<IOpaqueRegistrationService>(),
                RecoveryService = sp.GetRequiredService<ISecureKeyRecoveryService>(),
                LanguageDetectionService = sp.GetRequiredService<ILanguageDetectionService>(),
                Router = sp.GetRequiredService<IApplicationRouter>(),
                MainWindowViewModel = sp.GetRequiredService<Ecliptix.Core.ViewModels.Core.MainWindowViewModel>(),
                Settings = sp.GetRequiredService<DefaultSystemSettings>()
            }));
        services.AddTransient<MasterViewModel>();
    }

    private static string GetPlatformAppDataDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ApplicationConstants.Storage.LocalShareDirectory
            );
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ApplicationConstants.Storage.ApplicationSupportDirectory
        );
    }

    private static void SetSecurePermissionsIfUnix(string directory)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
            !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(directory, ApplicationConstants.FilePermissions.SecureDirectoryMode);
            Log.Debug(ApplicationConstants.Logging.PermissionsSetMessage, directory);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, ApplicationConstants.Logging.PermissionsFailMessage, directory);
        }
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException(ApplicationConstants.Logging.PathEmptyErrorMessage, nameof(path));
        }

        string appDataDir = GetPlatformAppDataDirectory();

        path = Environment.ExpandEnvironmentVariables(
            path.Replace(ApplicationConstants.Storage.AppDataEnvironmentVariable,
                Path.Combine(appDataDir, ApplicationConstants.Storage.EcliptixDirectoryName))
        );

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            return path;
        }

        Directory.CreateDirectory(directory);
        SetSecurePermissionsIfUnix(directory);

        return path;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().UseReactiveUI();
}
