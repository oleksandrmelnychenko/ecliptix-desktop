using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using DotNetEnv;
using Ecliptix.Core.Interceptors;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Network.RpcServices;
using Ecliptix.Core.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.Membership;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
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
            Log.Information("Application shutting down.");
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
        IConfigurationSection serilogConfig = configuration.GetSection("Serilog");
        LogEventLevel minimumLevel = serilogConfig.GetValue<string>("MinimumLevel:Default") switch
        {
            "Debug" => LogEventLevel.Debug,
            "Information" => LogEventLevel.Information,
            "Warning" => LogEventLevel.Warning,
            "Error" => LogEventLevel.Error,
            _ => LogEventLevel.Warning
        };

        LoggerConfiguration loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel);

        IEnumerable<IConfigurationSection> overrides = serilogConfig.GetSection("MinimumLevel:Override").GetChildren();
        foreach (IConfigurationSection overrideSection in overrides)
        {
            LogEventLevel level = overrideSection.Value switch
            {
                "Debug" => LogEventLevel.Debug,
                "Information" => LogEventLevel.Information,
                "Warning" => LogEventLevel.Warning,
                "Error" => LogEventLevel.Error,
                _ => LogEventLevel.Warning
            };
            loggerConfig.MinimumLevel.Override(overrideSection.Key, level);
        }

        IConfigurationSection? fileSink = serilogConfig.GetSection("WriteTo").GetChildren()
            .FirstOrDefault(s => s["Name"] == "Async")?.GetSection("Args:configure")
            .GetChildren().FirstOrDefault(c => c["Name"] == "File")?.GetSection("Args");
        if (fileSink != null)
        {
            string path = ResolvePath(fileSink["path"] ?? "Storage/logs/ecliptix.log");
            loggerConfig.WriteTo.Async(a => a.File(
                path: path,
                rollingInterval: fileSink.GetValue<RollingInterval>("rollingInterval", RollingInterval.Day),
                retainedFileCountLimit: fileSink.GetValue<int?>("retainedFileCountLimit", 7),
                fileSizeLimitBytes: fileSink.GetValue<long?>("fileSizeLimitBytes", 10000000),
                outputTemplate: fileSink["outputTemplate"] ??
                                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));
        }
        else
        {
            Log.Warning("No file sink configured in Serilog settings; logging to console only in development");
        }

        if (configuration.GetValue<string>("AppSettings:Environment") == "Development")
        {
            loggerConfig.WriteTo.Console();
        }

        return loggerConfig.CreateLogger();
    }

    private static IServiceCollection ConfigureServices(IConfiguration configuration)
    {
        ServiceCollection services = new();

        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        services.AddDataProtection()
            .SetApplicationName("Ecliptix")
            .PersistKeysToFileSystem(new DirectoryInfo(ResolvePath("%APPDATA%/Storage/DataProtection-Keys")))
            .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

        services.AddSingleton(configuration);
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<SecureStoreOptions>(options =>
        {
            IConfigurationSection section = configuration.GetSection("SecureStoreOptions");
            options.EncryptedStatePath = ResolvePath(section["EncryptedStatePath"] ?? "Storage/state");
            options.Validate();
        });
        
        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);
        services.AddSingleton<ApplicationInstanceSettings>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ILogger<SecureStorageProvider>>(sp =>
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SecureStorageProvider>());
        services.AddSingleton<ISecureStorageProvider, SecureStorageProvider>();
        services.AddSingleton<RpcServiceManager>();
        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<UnaryRpcServices>();
        services.AddSingleton<SecrecyChannelRpcServices>();
        services.AddSingleton<ReceiveStreamRpcServices>();
        services.AddSingleton<IRpcMetaDataProvider, RpcMetaDataProvider>();
        services.AddSingleton<RequestMetaDataInterceptor>();

        ConfigureGrpc(services);
        ConfigureViewModels(services);

        return services;
    }

    private static void ConfigureGrpc(IServiceCollection services)
    {
        Action<IServiceProvider, GrpcClientFactoryOptions> configureGrpcClient = (provider, options) =>
        {
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            string? endpoint = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? settings.DataCenterConnectionString
                : string.Empty;

            if (string.IsNullOrEmpty(endpoint))
                throw new InvalidOperationException("gRPC endpoint URL is not configured in appsettings.json.");

            options.Address = new Uri(endpoint);
        };

        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>(configureGrpcClient)
            .AddInterceptor<RequestMetaDataInterceptor>();
        services.AddGrpcClient<AuthVerificationServices.AuthVerificationServicesClient>(configureGrpcClient)
            .AddInterceptor<RequestMetaDataInterceptor>();
        services.AddGrpcClient<MembershipServices.MembershipServicesClient>(configureGrpcClient)
            .AddInterceptor<RequestMetaDataInterceptor>();
    }

    private static void ConfigureViewModels(IServiceCollection services)
    {
        services.AddTransient<AuthenticationViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<RegistrationWizardViewModel>();
        services.AddTransient<PhoneVerificationViewModel>();
        services.AddTransient<VerificationCodeEntryViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<PasswordConfirmationViewModel>();
        services.AddTransient<NicknameInputViewModel>();
        services.AddTransient<PassPhaseViewModel>();
        services.AddSingleton<AuthenticationViewFactory>();
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("Path cannot be empty.", nameof(path));

        string appDataDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library/Application Support");

        path = Environment.ExpandEnvironmentVariables(path.Replace("%APPDATA%", Path.Combine(appDataDir, "Ecliptix")));

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    File.SetUnixFileMode(directory,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    Log.Debug("Set secure permissions (700) on directory {Path}", directory);
                }
                catch (IOException ex)
                {
                    Log.Warning(ex, "Failed to set permissions for directory {Path}", directory);
                }
            }
        }

        return path;
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}