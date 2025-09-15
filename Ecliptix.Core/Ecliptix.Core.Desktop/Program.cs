using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using DotNetEnv;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Desktop.Constants;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Data.SecureStorage;
using Ecliptix.Core.Infrastructure.Data.SecureStorage.Configuration;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Connectivity;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Network.Core.State;
using Ecliptix.Core.Infrastructure.Network.Core.State.Configuration;
using Ecliptix.Core.Infrastructure.Network.Transport;
using Ecliptix.Core.Infrastructure.Network.Transport.Grpc;
using Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Platform;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.External.IpGeolocation;
using Ecliptix.Core.Services.Abstractions.External;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Features.Authentication;
using Ecliptix.Core.Features.Main;
using IViewLocator = Ecliptix.Core.Core.Abstractions.IViewLocator;
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

namespace Ecliptix.Core.Desktop;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        string mutexName = string.Format(ApplicationConstants.ApplicationSettings.MutexNameFormat, Environment.UserName);
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
            if (configuration[ApplicationConstants.ApplicationSettings.EnvironmentKey] != ApplicationConstants.ApplicationSettings.DevelopmentEnvironment)
                Environment.Exit(ApplicationConstants.ExitCodes.FatalError);
            throw;
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
            .AddJsonFile(string.Format(ApplicationConstants.Configuration.EnvironmentAppSettingsPattern, environment), optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static Logger ConfigureSerilog(IConfiguration configuration)
    {
        try
        {
            LoggerConfiguration loggerConfig = new();

            IConfigurationSection serilogSection = configuration.GetSection(ApplicationConstants.Configuration.SerilogSection);

            string minLevel = serilogSection[ApplicationConstants.Configuration.MinimumLevelDefaultKey] ?? ApplicationConstants.LogLevels.Information;
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

            string logPath = Path.Combine(ApplicationConstants.Storage.LogsDirectory, ApplicationConstants.Storage.LogFilePattern);
            loggerConfig = loggerConfig.WriteTo.File(logPath, rollingInterval: RollingInterval.Day);

            return loggerConfig.CreateLogger();
        }
        catch (Exception)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(Path.Combine(ApplicationConstants.Storage.LogsDirectory, ApplicationConstants.Storage.LogFilePattern), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
    }

    private static IServiceCollection ConfigureServices(IConfiguration configuration)
    {
        ServiceCollection services = new();

        ConfigureCoreServices(services, configuration);
        ConfigureNetworkServices(services, configuration);
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
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
    }

    private static void ConfigureNetworkServices(IServiceCollection services, IConfiguration configuration)
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
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(ApplicationConstants.Thresholds.ExponentialBackoffBase, attempt))))
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(ApplicationConstants.Timeouts.HttpTimeout));

        services.AddSingleton<InternetConnectivityObserver>();
        services.AddSingleton(new InternetConnectivityObserverOptions
        {
            PollingInterval = ApplicationConstants.Timeouts.DefaultPollingInterval,
            FailureThreshold = ApplicationConstants.Thresholds.DefaultFailureThreshold,
            SuccessThreshold = ApplicationConstants.Thresholds.DefaultSuccessThreshold
        });

        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<RequestDeduplicationService>(_ => new RequestDeduplicationService(ApplicationConstants.Timeouts.RequestDeduplicationTimeout));
        services.AddSingleton<IConnectionStateManager, ConnectionStateManager>();
        services.AddSingleton<IPendingRequestManager, PendingRequestManager>();
        services.AddSingleton<ConnectionStateConfiguration>();
    }

    private static void ConfigureSecurityServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IOptions<DefaultSystemSettings>>(_ =>
        {
            IConfigurationSection section = configuration.GetSection(ApplicationConstants.Configuration.DefaultAppSettingsSection);
            DefaultSystemSettings settings = new()
            {
                DefaultTheme = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DefaultTheme),
                Environment = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.Environment, ApplicationConstants.ApplicationSettings.ProductionEnvironment),
                DataCenterConnectionString = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DataCenterConnectionString),
                CountryCodeApi = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.CountryCodeApi),
                DomainName = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.DomainName),
                Culture = GetSectionValue(section, ApplicationConstants.ConfigurationKeys.Culture)
            };
            return Options.Create(settings);
        });

        services.AddSingleton<IOptions<SecureStoreOptions>>(_ =>
        {
            IConfigurationSection section = configuration.GetSection(ApplicationConstants.Configuration.SecureStoreOptionsSection);
            SecureStoreOptions options = new()
            {
                EncryptedStatePath = ResolvePath(
                    GetSectionValue(section, ApplicationConstants.ConfigurationKeys.EncryptedStatePath, ApplicationConstants.Storage.DefaultStatePath)
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

            string storagePath = config[ApplicationConstants.Configuration.SecureStorageSection + ApplicationConstants.Configuration.PathSeparator + ApplicationConstants.ConfigurationKeys.StatePath]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                ApplicationConstants.Storage.EcliptixDirectoryName, ApplicationConstants.Storage.SecureProtocolStateFile);

            byte[] deviceId = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);

            return new SecureProtocolStateStorage(platformProvider, storagePath, deviceId);
        });
    }

    private static void ConfigureMessagingServices(IServiceCollection services)
    {
        services.AddSingleton<IUnifiedMessageBus, UnifiedMessageBus>();
        services.AddSingleton<ISystemEventService, SystemEventService>();
        services.AddSingleton<INetworkEventService, NetworkEventService>();
        services.AddSingleton<IBottomSheetService, BottomSheetService>();
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<ISingleInstanceManager, SingleInstanceManager>();
        services.AddSingleton<IWindowActivationService, WindowActivationService>();
    }

    private static void ConfigureAuthenticationServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthenticationService, OpaqueAuthenticationService>();
        services.AddSingleton<IOpaqueRegistrationService, OpaqueRegistrationService>();
        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();
        services.AddSingleton<IRpcServiceManager, RpcServiceManager>();

        services.AddSingleton<IRetryStrategy>(sp =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            INetworkEventService networkEvents = sp.GetRequiredService<INetworkEventService>();
            IUiDispatcher uiDispatcher = sp.GetRequiredService<IUiDispatcher>();

            IConfigurationSection section = config.GetSection(ApplicationConstants.Configuration.ImprovedRetryPolicySection);
            ImprovedRetryConfiguration retryConfig = CreateRetryConfiguration(section);

            SecrecyChannelRetryStrategy retryStrategy = new(retryConfig, networkEvents, uiDispatcher);
            Lazy<NetworkProvider> lazyProvider = new(sp.GetRequiredService<NetworkProvider>);
            retryStrategy.SetLazyNetworkProvider(lazyProvider);
            return retryStrategy;
        });

        services.AddSingleton<IUnaryRpcServices, UnaryRpcServices>();
        services.AddSingleton<ISecrecyChannelRpcServices, SecrecyChannelRpcServices>();
        services.AddSingleton<IReceiveStreamRpcServices, ReceiveStreamRpcServices>();
        services.AddSingleton<IRpcMetaDataProvider, RpcMetaDataProvider>();
        services.AddSingleton<RequestMetaDataInterceptor>();
        services.AddSingleton<SecrecyChannelRetryInterceptor>();
    }

    private static ImprovedRetryConfiguration CreateRetryConfiguration(IConfigurationSection section)
    {
        return new ImprovedRetryConfiguration
        {
            InitialRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.InitialRetryDelay], out TimeSpan initialDelay)
                ? initialDelay : ApplicationConstants.Timeouts.DefaultInitialRetryDelay,
            MaxRetryDelay = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.MaxRetryDelay], out TimeSpan maxDelay)
                ? maxDelay : ApplicationConstants.Timeouts.DefaultMaxRetryDelay,
            MaxRetries = int.TryParse(section[ApplicationConstants.ConfigurationKeys.MaxRetries], out int maxRetries)
                ? maxRetries : ApplicationConstants.Thresholds.DefaultMaxRetries,
            UseAdaptiveRetry = !bool.TryParse(section[ApplicationConstants.ConfigurationKeys.UseAdaptiveRetry], out bool adaptive) || adaptive
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
            string endpoint = settings.Environment.Equals(ApplicationConstants.ApplicationSettings.DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase)
                ? settings.DataCenterConnectionString
                : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
                throw new InvalidOperationException(ApplicationConstants.Logging.GrpcEndpointErrorMessage);

            options.Address = new Uri(endpoint);
        }
    }

    private static void ConfigureModules(IServiceCollection services)
    {
        services.AddSingleton<ModuleDependencyResolver>();
        services.AddSingleton<ModuleResourceManager>();
        services.AddHostedService<ModuleResourceManager>(provider => provider.GetRequiredService<ModuleResourceManager>());

        services.AddSingleton<IModuleMessageBus, ModuleMessageBus>();
        services.AddSingleton<IModuleSharedState, ModuleSharedState>();
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
        services.AddSingleton<NetworkStatusNotificationViewModel>();
        services.AddTransient<SplashWindowViewModel>();
        services.AddTransient<MembershipHostWindowModel>();
        services.AddTransient<MainViewModel>();
    }

    private static string GetPlatformAppDataDirectory()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ApplicationConstants.Storage.LocalShareDirectory
                )
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ApplicationConstants.Storage.ApplicationSupportDirectory
                );
    }

    private static void SetSecurePermissionsIfUnix(string directory)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
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
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException(ApplicationConstants.Logging.PathEmptyErrorMessage, nameof(path));

        string appDataDir = GetPlatformAppDataDirectory();

        path = Environment.ExpandEnvironmentVariables(
            path.Replace(ApplicationConstants.Storage.AppDataEnvironmentVariable,
                        Path.Combine(appDataDir, ApplicationConstants.Storage.EcliptixDirectoryName))
        );

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return path;

        Directory.CreateDirectory(directory);
        SetSecurePermissionsIfUnix(directory);

        return path;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().UseReactiveUI();
}