using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using DotNetEnv;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Desktop.Constants;
using Ecliptix.Core.Features.Authentication;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Chats;
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
using Splat.Microsoft.Extensions.DependencyInjection;
using IViewLocator = Ecliptix.Core.Core.Abstractions.IViewLocator;

namespace Ecliptix.Core.Desktop;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        string mutexName =
            string.Format(ApplicationConstants.ApplicationSettings.MUTEX_NAME_FORMAT, Environment.UserName);
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
            Log.Information(ApplicationConstants.Logging.STARTUP_MESSAGE);
            IServiceCollection services = ConfigureServices(configuration);

            services.UseMicrosoftDependencyResolver();

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ReactiveUI.IViewLocator reactiveViewLocator = serviceProvider.GetRequiredService<ReactiveUI.IViewLocator>();
            Splat.Locator.CurrentMutable.Register(() => reactiveViewLocator, typeof(ReactiveUI.IViewLocator));

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, ApplicationConstants.Logging.FATAL_ERROR_MESSAGE);
            if (configuration[ApplicationConstants.ApplicationSettings.ENVIRONMENT_KEY] !=
                ApplicationConstants.ApplicationSettings.DEVELOPMENT_ENVIRONMENT)
            {
                Environment.Exit(ApplicationConstants.ExitCodes.FATAL_ERROR);
            }
        }
        finally
        {
            Log.Information(ApplicationConstants.Logging.SHUTDOWN_MESSAGE);
            await Log.CloseAndFlushAsync();
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        string? environment = Env.GetString(ApplicationConstants.ApplicationSettings.DOT_NET_ENVIRONMENT_KEY);
#if DEBUG
        environment ??= ApplicationConstants.ApplicationSettings.DEVELOPMENT_ENVIRONMENT;
#else
        environment ??= ApplicationConstants.ApplicationSettings.PRODUCTION_ENVIRONMENT;
#endif

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(ApplicationConstants.Configuration.APP_SETTINGS_FILE, optional: false, reloadOnChange: true)
            .AddJsonFile(string.Format(ApplicationConstants.Configuration.ENVIRONMENT_APP_SETTINGS_PATTERN, environment),
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
                configuration.GetSection(ApplicationConstants.Configuration.SERILOG_SECTION);

            string minLevel = serilogSection[ApplicationConstants.Configuration.MINIMUM_LEVEL_DEFAULT_KEY] ??
                              ApplicationConstants.LogLevels.INFORMATION;
            loggerConfig = minLevel switch
            {
                ApplicationConstants.LogLevels.DEBUG => loggerConfig.MinimumLevel.Debug(),
                ApplicationConstants.LogLevels.INFORMATION => loggerConfig.MinimumLevel.Information(),
                ApplicationConstants.LogLevels.WARNING => loggerConfig.MinimumLevel.Warning(),
                ApplicationConstants.LogLevels.ERROR => loggerConfig.MinimumLevel.Error(),
                ApplicationConstants.LogLevels.FATAL => loggerConfig.MinimumLevel.Fatal(),
                _ => loggerConfig.MinimumLevel.Information()
            };

            loggerConfig = loggerConfig.WriteTo.Console();

            string logPath = Path.Combine(ApplicationConstants.Storage.LOGS_DIRECTORY,
                ApplicationConstants.Storage.LOG_FILE_PATTERN);
            loggerConfig = loggerConfig.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);

            return loggerConfig.CreateLogger();
        }
        catch (Exception)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(
                    Path.Combine(ApplicationConstants.Storage.LOGS_DIRECTORY,
                        ApplicationConstants.Storage.LOG_FILE_PATTERN), rollingInterval: RollingInterval.Day)
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
            .SetApplicationName(ApplicationConstants.ApplicationSettings.APPLICATION_NAME)
            .PersistKeysToFileSystem(
                new DirectoryInfo(ResolvePath(ApplicationConstants.Storage.DATA_PROTECTION_KEYS_PATH))
            )
            .SetDefaultKeyLifetime(ApplicationConstants.Timeouts.DefaultKeyLifetime);

        services.AddSingleton(configuration);
        services.AddSingleton<IScheduler>(AvaloniaScheduler.Instance);
    }

    private static void ConfigureNetworkServices(IServiceCollection services)
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
                configuration.GetSection(ApplicationConstants.Configuration.DEFAULT_APP_SETTINGS_SECTION);
            DefaultSystemSettings settings = new()
            {
                DefaultTheme = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DEFAULT_THEME),
                Environment = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.ENVIRONMENT,
                    ApplicationConstants.ApplicationSettings.PRODUCTION_ENVIRONMENT),
                DataCenterConnectionString = GetSectionValue(section,
                    ApplicationConstants.ConfigurationKeys.DATA_CENTER_CONNECTION_STRING),
                CountryCodeApi = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.COUNTRY_CODE_API),
                DomainName = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DOMAIN_NAME),
                Culture = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.CULTURE),
                PrivacyPolicyUrl = GetSectionValue(section, "PrivacyPolicyUrl"),
                TermsOfServiceUrl = GetSectionValue(section, "TermsOfServiceUrl"),
                SupportUrl = GetSectionValue(section, "SupportUrl")
            };
            return Options.Create(settings);
        });

        services.AddSingleton<IOptions<SecureStoreOptions>>(_ =>
        {
            IConfigurationSection section =
                configuration.GetSection(ApplicationConstants.Configuration.SECURE_STORE_OPTIONS_SECTION);
            SecureStoreOptions options = new()
            {
                EncryptedStatePath = ResolvePath(
                    GetSectionValue(section, ApplicationConstants.ConfigurationKeys.ENCRYPTED_STATE_PATH,
                        ApplicationConstants.Storage.DEFAULT_STATE_PATH)
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
                ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME);
            return new CrossPlatformSecurityProvider(appDataPath);
        });

        services.AddSingleton<ISecureProtocolStateStorage>(sp =>
        {
            IPlatformSecurityProvider platformProvider = sp.GetRequiredService<IPlatformSecurityProvider>();
            IConfiguration config = sp.GetRequiredService<IConfiguration>();

            string storageDirectory =
                config[
                    ApplicationConstants.Configuration.SECURE_STORAGE_SECTION +
                    ApplicationConstants.Configuration.PATH_SEPARATOR + ApplicationConstants.ConfigurationKeys.STATE_PATH]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME);

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
                config.GetSection(ApplicationConstants.Configuration.SECRECY_CHANNEL_RETRY_POLICY_SECTION);
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
                settings.Environment.Equals(ApplicationConstants.ApplicationSettings.DEVELOPMENT_ENVIRONMENT,
                    StringComparison.OrdinalIgnoreCase)
                    ? settings.DataCenterConnectionString
                    : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException(ApplicationConstants.Logging.GRPC_ENDPOINT_ERROR_MESSAGE);
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
        catalog.AddModule<ChatModule>();

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
                ApplicationConstants.Storage.LOCAL_SHARE_DIRECTORY
            );
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ApplicationConstants.Storage.APPLICATION_SUPPORT_DIRECTORY
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
            File.SetUnixFileMode(directory, ApplicationConstants.FilePermissions.SECURE_DIRECTORY_MODE);
            Log.Debug(ApplicationConstants.Logging.PERMISSIONS_SET_MESSAGE, directory);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, ApplicationConstants.Logging.PERMISSIONS_FAIL_MESSAGE, directory);
        }
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException(ApplicationConstants.Logging.PATH_EMPTY_ERROR_MESSAGE, nameof(path));
        }

        string appDataDir = GetPlatformAppDataDirectory();

        path = Environment.ExpandEnvironmentVariables(
            path.Replace(ApplicationConstants.Storage.APP_DATA_ENVIRONMENT_VARIABLE,
                Path.Combine(appDataDir, ApplicationConstants.Storage.ECLIPTIX_DIRECTORY_NAME))
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
