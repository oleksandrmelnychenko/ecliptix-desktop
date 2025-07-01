using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.ReactiveUI;
using Ecliptix.Core;
using Ecliptix.Core.Interceptors;
using Ecliptix.Core.Network;
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
using Google.Protobuf;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using Splat.Microsoft.Extensions.DependencyInjection;

public sealed class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        try
        {
            IConfigurationRoot configuration = BuildConfiguration();
            ServiceCollection services = new();

            services.AddLogging(builder => builder.AddSerilog());
            services.Configure<SecureStoreOptions>(options =>
            {
                options.StorePath = Path.Combine(AppContext.BaseDirectory, "secure_data.bin");
                options.KeyPath = Path.Combine(AppContext.BaseDirectory, "secure_data.key");
            });


            services.AddSingleton<ISecureStorageProvider, SecureSecureStorageProvider>();

            ConfigureServices(services, configuration);
            services.AddSingleton<NetworkRpcServiceManager>();

            services.UseMicrosoftDependencyResolver();

            Log.Information("Initialization complete. Starting Avalonia application...");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            Log.Fatal(e, "Application terminated unexpectedly during startup or runtime.");
        }
        finally
        {
            Log.Information("Application shutting down.");
            await Log.CloseAndFlushAsync();
        }
    }

    private static IConfigurationRoot BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();


    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(builder => builder.AddSerilog());

        services.AddSingleton(configuration);
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);

        services.AddSingleton<ApplicationInstanceSettings>();
        services.AddSingleton<ISecureStorageProvider, SecureSecureStorageProvider>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.Configure<SecureStoreOptions>(options =>
        {
            options.StorePath = Path.Combine(AppContext.BaseDirectory, "secure_data.bin");
            options.KeyPath = Path.Combine(AppContext.BaseDirectory, "secure_data.key");
        });

        ConfigureNetworkServices(services);
        ConfigureGrpc(services);

        ConfigureViewModels(services);
    }

    private static void ConfigureNetworkServices(IServiceCollection services)
    {
        services.AddSingleton<NetworkProvider>();
        services.AddSingleton<UnaryRpcServices>();
        services.AddSingleton<SecrecyChannelRpcServices>();
        services.AddSingleton<ReceiveStreamRpcServices>();
    }

    private static void ConfigureGrpc(IServiceCollection services)
    {
        services.AddSingleton<IClientStateProvider, ClientStateProvider>();
        services.AddSingleton<RequestMetaDataInterceptor>();

        Action<IServiceProvider, GrpcClientFactoryOptions> configureGrpcClient = (provider, options) =>
        {
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            string? endpoint = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? settings.LocalHostUrl
                : settings.CloudHostUrl;

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

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}