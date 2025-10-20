using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.AutoUpdater;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;

namespace Ecliptix.Desktop.Integration;

/// <summary>
/// Example Program.cs showing Serilog integration with UpdateManager
/// </summary>
public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Build configuration first
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Configure Serilog from configuration
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Ecliptix.Desktop")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .WriteTo.File(
                path: "logs/ecliptix-.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("===============================================");
            Log.Information("Starting Ecliptix Desktop Application");
            Log.Information("Version: {Version}", GetApplicationVersion());
            Log.Information("Environment: {Environment}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
            Log.Information("===============================================");

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
            throw;
        }
        finally
        {
            Log.Information("Application shutting down");
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Setup DI container
        var services = new ServiceCollection();

        // Register configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Register Serilog logger
        services.AddSingleton(Log.Logger);

        // Register update configuration
        var updateConfig = configuration.GetSection("UpdateService").Get<UpdateConfiguration>()
            ?? new UpdateConfiguration
            {
                UpdateServerUrl = "https://updates.ecliptix.com",
                EnableAutoCheck = true,
                CheckInterval = TimeSpan.FromHours(6)
            };

        services.AddSingleton(updateConfig);

        Log.Information("Update service configured: {@UpdateConfig}", new
        {
            updateConfig.UpdateServerUrl,
            updateConfig.EnableAutoCheck,
            updateConfig.CheckInterval
        });

        // Register update manager with Serilog logger
        services.AddSingleton<UpdateManager>(sp =>
        {
            var config = sp.GetRequiredService<UpdateConfiguration>();
            var logger = sp.GetRequiredService<ILogger>();

            Log.Information("Initializing UpdateManager");
            return new UpdateManager(config, logger);
        });

        // Register ViewModels
        services.AddTransient<UpdateViewModel>();
        services.AddTransient<MainWindowViewModel>();

        // Build service provider
        var serviceProvider = services.BuildServiceProvider();

        // Configure Avalonia
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .With(new Win32PlatformOptions
            {
                UseWgl = true,
                AllowEglInitialization = true
            })
            .AfterSetup(builder =>
            {
                // Store service provider for access in App
                ((App)builder.Instance!).ServiceProvider = serviceProvider;
            });
    }

    private static string GetApplicationVersion()
    {
        return typeof(Program).Assembly.GetName().Version?.ToString() ?? "Unknown";
    }
}

/// <summary>
/// Example App.axaml.cs with UpdateManager initialization
/// </summary>
public partial class App : Application
{
    public IServiceProvider? ServiceProvider { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Log.Information("Initializing desktop application");

            try
            {
                // Get services
                var updateManager = ServiceProvider?.GetService<UpdateManager>();
                if (updateManager == null)
                {
                    Log.Warning("UpdateManager not available");
                }
                else
                {
                    Log.Information("UpdateManager initialized successfully");

                    // Subscribe to update events for logging
                    updateManager.UpdateAvailable += OnUpdateAvailable;
                    updateManager.ErrorOccurred += OnUpdateError;
                }

                // Create main window with view model
                var mainViewModel = ServiceProvider?.GetService<MainWindowViewModel>()
                    ?? new MainWindowViewModel(updateManager!);

                desktop.MainWindow = new MainWindow
                {
                    DataContext = mainViewModel
                };

                Log.Information("Main window created successfully");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to initialize application");
                throw;
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnUpdateAvailable(object? sender, UpdateCheckResult result)
    {
        Log.Information(
            "Update available: {CurrentVersion} -> {LatestVersion}, Critical: {IsCritical}",
            result.CurrentVersion,
            result.LatestVersion,
            result.IsCritical
        );

        if (result.IsCritical)
        {
            Log.Warning("CRITICAL UPDATE REQUIRED: {Version}", result.LatestVersion);
        }
    }

    private void OnUpdateError(object? sender, string error)
    {
        Log.Error("Update check failed: {Error}", error);
    }
}

/// <summary>
/// Example MainWindowViewModel with UpdateViewModel
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private UpdateViewModel? _updateViewModel;

    public MainWindowViewModel(UpdateManager updateManager)
    {
        _logger = Log.ForContext<MainWindowViewModel>();
        _logger.Information("MainWindowViewModel initializing");

        // Create update view model
        UpdateViewModel = new UpdateViewModel(updateManager);

        // Check for updates on startup (delayed)
        _ = CheckForUpdatesOnStartupAsync();
    }

    public UpdateViewModel UpdateViewModel
    {
        get => _updateViewModel;
        private set => this.RaiseAndSetIfChanged(ref _updateViewModel, value);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            // Wait for app to fully load
            await Task.Delay(TimeSpan.FromSeconds(5));

            _logger.Information("Performing startup update check");
            await UpdateViewModel.CheckForUpdatesCommand.Execute();
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Startup update check failed");
        }
    }
}
