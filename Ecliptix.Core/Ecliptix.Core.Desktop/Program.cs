using System;
using System.Net.Http;
using System.Threading.Channels;
using Avalonia;
using Avalonia.ReactiveUI;
using Ecliptix.Core;
using Ecliptix.Core.Interceptors;
using Ecliptix.Core.Network;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.ViewModels.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

internal sealed class Program
{
    private static IServiceProvider? Services { get; set; }

    [STAThread]
    public static void Main(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        ServiceCollection services = new();
        services.UseMicrosoftDependencyResolver();
        IMutableDependencyResolver locator = Locator.CurrentMutable;
        locator.InitializeSplat();
        locator.InitializeReactiveUI();


        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
#pragma warning disable IL2026 // Suppress trim warning
#pragma warning disable IL3050 // Suppress AOT warning
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
#pragma warning restore IL3050
#pragma warning restore IL2026
        services.AddSingleton<AppSettings>(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);
        services.AddSingleton<ApplicationController>();
        services.AddSingleton<MembershipViewFactory>();
        services.AddTransient<AuthenticationViewModel>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignUpHostViewModel>();
        services.AddTransient<VerifyMobileViewModel>();
        
        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug));

        services.AddHttpClient();

        services.AddSingleton(provider =>
        {
            AppSettings settings = provider.GetRequiredService<IOptions<AppSettings>>().Value;
            ApplicationController applicationController = provider.GetRequiredService<ApplicationController>();
            bool isDevelopment = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase);
            
            string endpointUrl = isDevelopment ? settings.LocalHostUrl : settings.CloudHostUrl;
            if (string.IsNullOrEmpty(endpointUrl))
            {
                throw new InvalidOperationException("Required endpoint URL not configured.");
            }

            GrpcChannel channel = GrpcChannel.ForAddress(endpointUrl, new GrpcChannelOptions
            {
                UnsafeUseInsecureChannelCallCredentials = true,
            });

            Interceptor[] interceptors =
            [
                new RequestMetaDataInterceptor(applicationController.AppInstanceId,applicationController.DeviceId)
            ];

            CallInvoker interceptedChannel = channel.Intercept(interceptors);
            return new AppDeviceServiceActions.AppDeviceServiceActionsClient(interceptedChannel);
        });

        services.AddTransient<MainViewModel>();
       
        services.AddSingleton<ILogManager>(new DefaultLogManager());

        Services = services.BuildServiceProvider();
        Locator.CurrentMutable.RegisterConstant(Services, typeof(IServiceProvider));

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        (Services as IDisposable)?.Dispose();
    }

    private static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}