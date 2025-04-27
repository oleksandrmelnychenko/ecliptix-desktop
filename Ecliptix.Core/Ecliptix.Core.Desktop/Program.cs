using System;
using System.Net.Http;
using Avalonia;
using Avalonia.ReactiveUI;
using Ecliptix.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Protobuf.AppDeviceServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        services.AddSingleton<IConfiguration>(configuration);
        services.AddOptions();
#pragma warning disable IL2026 // Suppress trim warning
#pragma warning disable IL3050 // Suppress AOT warning
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
#pragma warning restore IL3050
#pragma warning restore IL2026
        services.AddSingleton<AppSettings>(sp => sp.GetRequiredService<IOptions<AppSettings>>().Value);

        services.AddLogging(builder => builder.AddDebug().SetMinimumLevel(LogLevel.Debug)); // Added Debug provider

        services.AddHttpClient();
        services.AddGrpcClient<AppDeviceServiceActions.AppDeviceServiceActionsClient>((serviceProvider, options) =>
            {
                AppSettings settings = serviceProvider.GetRequiredService<IOptions<AppSettings>>().Value;
                bool isDevelopment = settings.Environment.Equals("Development", StringComparison.OrdinalIgnoreCase);
                string? endpointUrl = (isDevelopment ? settings.LocalHostUrl : settings.CloudHostUrl);
                if (string.IsNullOrEmpty(endpointUrl))
                    throw new InvalidOperationException("Required endpoint URL not configured.");
                options.Address = new Uri(endpointUrl);
            })
            .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
            {
                HttpClientHandler handler = new();
                return handler;
            });

        services.AddTransient<MainViewModel>();

        services.AddSingleton<ILogManager>(new DefaultLogManager());

        Services = services.BuildServiceProvider();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

       // (Services as IDisposable)?.Dispose();
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI(); // Calls Splat initialization internally
    }
}