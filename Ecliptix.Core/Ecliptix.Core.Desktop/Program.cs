using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reactive.Concurrency;
using System.Reflection;
using System.Text;
using System.Runtime.InteropServices;
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
using Ecliptix.Core.Network.Contracts.Core;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Network.Core;
using Ecliptix.Core.Network.Core.Configuration;
using Ecliptix.Core.Network.Core.Connectivity;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services;
using Ecliptix.Core.Network.Services.Retry;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Core.Network.Transport;
using Ecliptix.Core.Network.Transport.Grpc.Interceptors;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Security;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.IpGeolocation;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
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
        IConfiguration configuration = BuildConfiguration();
        Env.Load();
        Log.Logger = ConfigureSerilog(configuration);

        try
        {
            Log.Information(ApplicationConstants.Logging.StartupMessage);
            IServiceCollection services = ConfigureServices(configuration);
            services.UseMicrosoftDependencyResolver();

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
        services.AddSingleton<RequestDeduplicationService>(sp =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            IConfigurationSection section = config.GetSection(ApplicationConstants.Configuration.ImprovedRetryPolicySection);

            ImprovedRetryConfiguration retryConfig = CreateRetryConfiguration(section);
            return new RequestDeduplicationService(retryConfig.RequestDeduplicationWindow);
        });

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
        ConfigureViewModels(services);

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
            CircuitBreakerThreshold = int.TryParse(section[ApplicationConstants.ConfigurationKeys.CircuitBreakerThreshold], out int threshold)
                ? threshold : ApplicationConstants.Thresholds.DefaultCircuitBreakerThreshold,
            CircuitBreakerDuration = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.CircuitBreakerDuration], out TimeSpan duration)
                ? duration : ApplicationConstants.Timeouts.DefaultCircuitBreakerDuration,
            RequestDeduplicationWindow = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.RequestDeduplicationWindow], out TimeSpan window)
                ? window : ApplicationConstants.Timeouts.DefaultRequestDeduplicationWindow,
            UseAdaptiveRetry = !bool.TryParse(section[ApplicationConstants.ConfigurationKeys.UseAdaptiveRetry], out bool adaptive) || adaptive,
            HealthCheckTimeout = TimeSpan.TryParse(section[ApplicationConstants.ConfigurationKeys.HealthCheckTimeout], out TimeSpan timeout)
                ? timeout : ApplicationConstants.Timeouts.DefaultHealthCheckTimeout
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

    private static void ConfigureViewModels(IServiceCollection services)
    {
        services.AddTransient<MembershipHostWindowModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<MobileVerificationViewModel>();
        services.AddTransient<VerifyOtpViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<PasswordConfirmationViewModel>();
        services.AddTransient<PassPhaseViewModel>();
        services.AddTransient<SplashWindowViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<LanguageSelectorViewModel>();
        services.AddSingleton<BottomSheetViewModel>();
        services.AddSingleton<NetworkStatusNotificationViewModel>();
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