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
            Log.Information("Starting Ecliptix application...");
            IServiceCollection services = ConfigureServices(configuration);
            services.UseMicrosoftDependencyResolver();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly during startup or runtime");
            if (configuration["AppSettings:Environment"] != "Development")
                Environment.Exit(1);
            throw;
        }
        finally
        {
            Log.Information("Application shutting down");
            await Log.CloseAndFlushAsync();
        }
    }

    private static IConfiguration BuildConfiguration()
    {
        string? environment = Env.GetString("DOTNET_ENVIRONMENT");
#if DEBUG
        environment ??= "Development";
#else
        environment ??= "Production";
#endif

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
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
                assemblies.Add(Assembly.Load("Serilog.Sinks.Console"));
            }
            catch { /* Console sink not available */ }
            
            try
            {
                assemblies.Add(Assembly.Load("Serilog.Sinks.File"));
            }
            catch { /* File sink not available */ }

            ConfigurationReaderOptions options = new(assemblies.ToArray());
            
            LoggerConfiguration loggerConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration, options);

            return loggerConfig.CreateLogger();
        }
        catch (Exception)
        {
            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs/ecliptix-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
    }

    private static IServiceCollection ConfigureServices(IConfiguration configuration)
    {
        ServiceCollection services = new();

        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        services
            .AddDataProtection()
            .SetApplicationName("Ecliptix")
            .PersistKeysToFileSystem(
                new DirectoryInfo(ResolvePath("%APPDATA%/Storage/DataProtection-Keys"))
            )
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        services.AddSingleton(configuration);
        services.AddSingleton<IOptions<DefaultSystemSettings>>(_ =>
        {
            IConfigurationSection section = configuration.GetSection("DefaultAppSettings");
            DefaultSystemSettings settings = new()
            {
                DefaultTheme = section["DefaultTheme"] ?? string.Empty,
                Environment = section["Environment"] ?? "Production",
                DataCenterConnectionString = section["DataCenterConnectionString"] ?? string.Empty,
                CountryCodeApi = section["CountryCodeApi"] ?? string.Empty,
                DomainName = section["DomainName"] ?? string.Empty,
                Culture = section["Culture"] ?? string.Empty
            };
            return Options.Create(settings);
        });
        services.AddSingleton<IOptions<SecureStoreOptions>>(_ =>
        {
            IConfigurationSection section = configuration.GetSection("SecureStoreOptions");
            SecureStoreOptions options = new()
            {
                EncryptedStatePath = ResolvePath(
                    section["EncryptedStatePath"] ?? "Storage/state"
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
            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync( 
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt))))
            .AddPolicyHandler(Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(5)));

        services.AddSingleton<IScheduler>(AvaloniaScheduler.Instance);
        services.AddSingleton<InternetConnectivityObserver>();
        services.AddSingleton(new InternetConnectivityObserverOptions
        {
            PollingInterval = TimeSpan.FromSeconds(10),
            FailureThreshold = 2,
            SuccessThreshold = 1
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
                "Ecliptix");
            return new CrossPlatformSecurityProvider(appDataPath);
        });
        services.AddSingleton<ISecureProtocolStateStorage>(sp =>
        {
            IPlatformSecurityProvider platformProvider = sp.GetRequiredService<IPlatformSecurityProvider>();
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            
            string storagePath = config["SecureStorage:StatePath"] 
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                                "Ecliptix", "secure_protocol_state.enc");
            
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
            IConfigurationSection section = config.GetSection("ImprovedRetryPolicy");
            
            ImprovedRetryConfiguration retryConfig = new()
            {
                InitialRetryDelay = TimeSpan.TryParse(section["InitialRetryDelay"], out var initialDelay) 
                    ? initialDelay : TimeSpan.FromSeconds(5),
                MaxRetryDelay = TimeSpan.TryParse(section["MaxRetryDelay"], out var maxDelay) 
                    ? maxDelay : TimeSpan.FromMinutes(2),
                MaxRetries = int.TryParse(section["MaxRetries"], out var maxRetries) 
                    ? maxRetries : 10,
                CircuitBreakerThreshold = int.TryParse(section["CircuitBreakerThreshold"], out var threshold) 
                    ? threshold : 5,
                CircuitBreakerDuration = TimeSpan.TryParse(section["CircuitBreakerDuration"], out var duration) 
                    ? duration : TimeSpan.FromMinutes(1),
                RequestDeduplicationWindow = TimeSpan.TryParse(section["RequestDeduplicationWindow"], out var window) 
                    ? window : TimeSpan.FromSeconds(10),
                UseAdaptiveRetry = bool.TryParse(section["UseAdaptiveRetry"], out var adaptive) 
                    ? adaptive : true,
                HealthCheckTimeout = TimeSpan.TryParse(section["HealthCheckTimeout"], out var timeout) 
                    ? timeout : TimeSpan.FromSeconds(5)
            };
            
            return new RequestDeduplicationService(retryConfig.RequestDeduplicationWindow);
        });
        
        services.AddSingleton<IRetryStrategy>(sp =>
        {
            IConfiguration config = sp.GetRequiredService<IConfiguration>();
            SecrecyChannelRetryStrategy retryStrategy = new(config);
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

    private static void ConfigureGrpc(IServiceCollection services)
    {
        services.AddSingleton((Action<GrpcClientFactoryOptions>)ConfigureClientOptions);
        services.AddConfiguredGrpcClients();
        return;

        void ConfigureClientOptions(GrpcClientFactoryOptions options)
        {
            DefaultSystemSettings settings = services.BuildServiceProvider()
                .GetRequiredService<DefaultSystemSettings>();
            string endpoint = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? settings.DataCenterConnectionString
                : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
                throw new InvalidOperationException("gRPC endpoint URL is not configured in appsettings.json.");

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
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        string appDataDir =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".local/share"
                    )
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library/Application Support"
                    );

        path = Environment.ExpandEnvironmentVariables(
            path.Replace("%APPDATA%", Path.Combine(appDataDir, "Ecliptix"))
        );

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory)) return path;
        Directory.CreateDirectory(directory);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return path;
        try
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            Log.Debug("Set secure permissions (700) on directory {Path}", directory);
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "Failed to set permissions for directory {Path}", directory);
        }

        return path;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().UseReactiveUI();
}