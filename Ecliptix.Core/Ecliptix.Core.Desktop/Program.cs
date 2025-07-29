using System;
using System.IO;
using System.Reactive.Concurrency;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using DotNetEnv;
using Ecliptix.Core.AppEvents;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Controls.LanguageSwitcher;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Interceptors;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Network.ResilienceStrategy;
using Ecliptix.Core.Network.RpcServices;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
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
            if (configuration.GetValue<string>("AppSettings:Environment") != "Development")
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
        LoggerConfiguration loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration);

        return loggerConfig.CreateLogger();
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
        services.Configure<DefaultSystemSettings>(configuration.GetSection("DefaultAppSettings"));
        services.Configure<SecureStoreOptions>(options =>
        {
            IConfigurationSection section = configuration.GetSection("SecureStoreOptions");
            options.EncryptedStatePath = ResolvePath(
                section["EncryptedStatePath"] ?? "Storage/state"
            );
        });

        services.AddHttpClient(InternetConnectivityObserver.HttpClientName, client =>
        {
            InternetConnectivityObserverOptions options = InternetConnectivityObserverOptions.Default;
            client.Timeout = options.ProbeTimeout;
        });

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

        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<DefaultSystemSettings>>().Value);
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ILogger<SecureStorageProvider>>(sp =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SecureStorageProvider>()
        );
        services.AddSingleton<ISecureStorageProvider, SecureStorageProvider>();
        services.AddSingleton<RpcServiceManager>();
        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<UnaryRpcServices>();
        services.AddSingleton<SecrecyChannelRpcServices>();
        services.AddSingleton<ReceiveStreamRpcServices>();
        services.AddSingleton<IRpcMetaDataProvider, RpcMetaDataProvider>();
        services.AddSingleton<RequestMetaDataInterceptor>();
        services.AddSingleton<DeadlineInterceptor>();
        services.AddTransient<ResilienceInterceptor>();
       
        ConfigureGrpc(services);
        ConfigureViewModels(services);

        return services;
    }

    private static void ConfigureGrpc(IServiceCollection services)
    {
        void ConfigureClientOptions(GrpcClientFactoryOptions options)
        {
            DefaultSystemSettings settings = services.BuildServiceProvider()
                .GetRequiredService<DefaultSystemSettings>();
            string? endpoint = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? settings.DataCenterConnectionString
                : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
                throw new InvalidOperationException("gRPC endpoint URL is not configured in appsettings.json.");

            options.Address = new Uri(endpoint);
        }

        services.AddSingleton((Action<GrpcClientFactoryOptions>)ConfigureClientOptions);
        services.AddConfiguredGrpcClients();
    }

    private static void ConfigureViewModels(IServiceCollection services)
    {
        services.AddTransient<MembershipHostWindowModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<MobileVerificationViewModel>();
        services.AddTransient<VerificationCodeEntryViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<PasswordConfirmationViewModel>();
        services.AddTransient<PassPhaseViewModel>();
        services.AddTransient<SplashWindowViewModel>();
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<LanguageSwitcherViewModel>();
        services.AddTransient<BottomSheetViewModel>();
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