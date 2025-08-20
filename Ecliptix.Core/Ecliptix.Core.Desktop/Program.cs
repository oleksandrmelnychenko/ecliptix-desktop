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
using Ecliptix.Core.AppEvents;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Controls;
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
// TODO: ViewModels moved to feature modules
// using Ecliptix.Core.ViewModels.Authentication.Registration;
// using Ecliptix.Core.ViewModels.Memberships;
// using Ecliptix.Core.ViewModels.Memberships.SignIn;
// using Ecliptix.Core.ViewModels.Memberships.SignUp;
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
        string mutexName = $"EcliptixDesktop_{Environment.UserName}";
        using Mutex mutex = new(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            Log.Information("Another instance is already running - exiting");
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

            // Configure ReactiveUI's ViewLocator after DI is set up
            IServiceProvider serviceProvider = services.BuildServiceProvider();
            ReactiveUI.IViewLocator reactiveViewLocator = serviceProvider.GetRequiredService<ReactiveUI.IViewLocator>();
            Splat.Locator.CurrentMutable.Register(() => reactiveViewLocator, typeof(ReactiveUI.IViewLocator));

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, ApplicationConstants.Logging.FatalErrorMessage);
            if (configuration[ApplicationConstants.ApplicationSettings.EnvironmentKey] != ApplicationConstants.ApplicationSettings.DevelopmentEnvironment)
                Environment.Exit(1);
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
            List<Assembly> assemblies = [];

            try
            {
                assemblies.Add(Assembly.Load(ApplicationConstants.Logging.ConsoleSinkAssembly));
            }
            catch { }

            try
            {
                assemblies.Add(Assembly.Load(ApplicationConstants.Logging.FileSinkAssembly));
            }
            catch { }

            ConfigurationReaderOptions options = new(assemblies.ToArray());

            LoggerConfiguration loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration, options);

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

        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationConstants.ApplicationSettings.ApplicationName)
            .PersistKeysToFileSystem(
                new DirectoryInfo(ResolvePath(ApplicationConstants.Storage.DataProtectionKeysPath))
            )
            .SetDefaultKeyLifetime(ApplicationConstants.Timeouts.DefaultKeyLifetime);

        services.AddSingleton(configuration);
        services.AddSingleton<IOptions<DefaultSystemSettings>>(_ =>
        {
            IConfigurationSection section = configuration.GetSection(ApplicationConstants.Configuration.DefaultAppSettingsSection);
            DefaultSystemSettings settings = new()
            {
                DefaultTheme = section[ApplicationConstants.ConfigurationKeys.DefaultTheme] ?? string.Empty,
                Environment = section[ApplicationConstants.ConfigurationKeys.Environment] ?? ApplicationConstants.ApplicationSettings.ProductionEnvironment,
                DataCenterConnectionString = section[ApplicationConstants.ConfigurationKeys.DataCenterConnectionString] ?? string.Empty,
                CountryCodeApi = section[ApplicationConstants.ConfigurationKeys.CountryCodeApi] ?? string.Empty,
                DomainName = section[ApplicationConstants.ConfigurationKeys.DomainName] ?? string.Empty,
                Culture = section[ApplicationConstants.ConfigurationKeys.Culture] ?? string.Empty
            };
            return Options.Create(settings);
        });
        services.AddSingleton<IOptions<SecureStoreOptions>>(_ =>
        {
            IConfigurationSection section = configuration.GetSection(ApplicationConstants.Configuration.SecureStoreOptionsSection);
            SecureStoreOptions options = new()
            {
                EncryptedStatePath = ResolvePath(
                    section[ApplicationConstants.ConfigurationKeys.EncryptedStatePath] ?? ApplicationConstants.Storage.DefaultStatePath
                )
            };
            return Options.Create(options);
        });

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
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))))
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(ApplicationConstants.Timeouts.HttpTimeout));

        services.AddSingleton<IScheduler>(AvaloniaScheduler.Instance);
        services.AddSingleton<InternetConnectivityObserver>();
        services.AddSingleton(new InternetConnectivityObserverOptions
        {
            PollingInterval = ApplicationConstants.Timeouts.DefaultPollingInterval,
            FailureThreshold = ApplicationConstants.Thresholds.DefaultFailureThreshold,
            SuccessThreshold = ApplicationConstants.Thresholds.DefaultSuccessThreshold
        });

        services.AddSingleton<IEventAggregator, EventAggregator>();
        services.AddSingleton<INetworkEvents, NetworkEvents>();
        services.AddSingleton<ISystemEvents, SystemEvents>();
        services.AddSingleton<IBottomSheetEvents, BottomSheetEvents>();

        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IAuthenticationService, OpaqueAuthenticationService>();
        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();

        services.AddSingleton<ISingleInstanceManager, SingleInstanceManager>();
        services.AddSingleton<IWindowActivationService, WindowActivationService>();

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

            string storagePath = config[ApplicationConstants.Configuration.SecureStorageSection + ":" + ApplicationConstants.ConfigurationKeys.StatePath]
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                ApplicationConstants.Storage.EcliptixDirectoryName, ApplicationConstants.Storage.SecureProtocolStateFile);

            byte[] deviceId = Encoding.UTF8.GetBytes(Environment.MachineName + Environment.UserName);

            return new SecureProtocolStateStorage(platformProvider, storagePath, deviceId);
        });
        services.AddSingleton<IRpcServiceManager, RpcServiceManager>();

        services.AddSingleton<ConnectionStateConfiguration>();

        services.AddSingleton<IConnectionStateManager, ConnectionStateManager>();
        services.AddSingleton<IPendingRequestManager, PendingRequestManager>();

        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<RequestDeduplicationService>(_ => new RequestDeduplicationService(TimeSpan.FromSeconds(10)));

        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        services.AddSingleton<IRetryStrategy>(sp =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            INetworkEvents networkEvents = sp.GetRequiredService<INetworkEvents>();
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

        ConfigureGrpc(services);
        ConfigureModules(services);

        return services;
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

        // Register ViewLocator for ReactiveUI navigation
        services.AddSingleton<IViewLocator, ViewLocator>();
        services.AddSingleton<ReactiveUiViewLocatorAdapter>();

        // Configure ReactiveUI to use our ViewLocator
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

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException(ApplicationConstants.Logging.PathEmptyErrorMessage, nameof(path));

        string appDataDir =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
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

        path = Environment.ExpandEnvironmentVariables(
            path.Replace("%APPDATA%", Path.Combine(appDataDir, ApplicationConstants.Storage.EcliptixDirectoryName))
        );

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return path;
        Directory.CreateDirectory(directory);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return path;
        try
        {
            File.SetUnixFileMode(directory, ApplicationConstants.FilePermissions.SecureDirectoryMode);
            Log.Debug(ApplicationConstants.Logging.PermissionsSetMessage, directory);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, ApplicationConstants.Logging.PermissionsFailMessage, directory);
        }

        return path;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().UseReactiveUI();
}