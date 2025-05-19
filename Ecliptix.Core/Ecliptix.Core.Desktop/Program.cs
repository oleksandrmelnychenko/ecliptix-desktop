using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Ecliptix.Core;
using Ecliptix.Core.Interceptors;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Protobuf.AppDeviceServices;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Serilog;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;

public sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        IConfigurationRoot configuration = BuildConfiguration();

        ServiceCollection services = new();

        services.UseMicrosoftDependencyResolver();

        ConfigureServices(services, configuration);

        ServiceProvider serviceProvider = services.BuildServiceProvider();

        //serviceProvider.UseMicrosoftDependencyResolver();

        BuildAvaloniaApp()
            .UseReactiveUI() 
            .StartWithClassicDesktopLifetime(args);

        if (serviceProvider is IDisposable disposable)
            disposable.Dispose();
    }

    private static IConfigurationRoot BuildConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", false)
            .AddEnvironmentVariables()
            .Build();

    private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(configuration);
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);

        services.AddSingleton<AppInstanceInfo>();

        services.AddTransient<AuthenticationViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<RegistrationWizardViewModel>();
        services.AddTransient<PhoneVerificationViewModel>();
        services.AddTransient<VerificationCodeEntryViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<PasswordConfirmationViewModel>();
        services.AddTransient<NicknameInputViewModel>();

        services.AddSingleton<AuthenticationViewFactory>();

        services.AddLogging(builder =>
            builder.AddSerilog(new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs/app.log", rollingInterval: RollingInterval.Day)
                .CreateLogger()));

        services.AddHttpClient();

        ConfigureGrpcClients(services);
        ConfigureNetworkServices(services);
    }

    private static void ConfigureGrpcClients(IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            AppSettings settings = provider.GetRequiredService<AppSettings>();
            AppInstanceInfo appInfo = provider.GetRequiredService<AppInstanceInfo>();
            string? endpoint = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                ? settings.LocalHostUrl
                : settings.CloudHostUrl;

            if (string.IsNullOrEmpty(endpoint))
                throw new InvalidOperationException("gRPC endpoint URL is not configured.");

            GrpcChannel channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
            {
                UnsafeUseInsecureChannelCallCredentials = true
            });

            CallInvoker interceptedInvoker = channel.Intercept(
                new RequestMetaDataInterceptor(appInfo.AppInstanceId, appInfo.DeviceId));

            return new GrpcClients(interceptedInvoker);
        });

        services.AddSingleton(sp => sp.GetRequiredService<GrpcClients>().AppDeviceServiceClient);
        services.AddSingleton(sp => sp.GetRequiredService<GrpcClients>().AuthenticationServiceClient);
    }

    private static void ConfigureNetworkServices(IServiceCollection services)
    {
        services.AddSingleton<NetworkController>();
        services.AddSingleton<NetworkServiceManager>();
        services.AddSingleton<SingleCallExecutor>();
        services.AddSingleton<ReceiveStreamExecutor>();
        services.AddSingleton<KeyExchangeExecutor>();
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

internal sealed class GrpcClients(CallInvoker callInvoker)
{
    public AppDeviceServiceActions.AppDeviceServiceActionsClient AppDeviceServiceClient { get; } = new(callInvoker);
    public AuthenticationServices.AuthenticationServicesClient AuthenticationServiceClient { get; } = new(callInvoker);
}