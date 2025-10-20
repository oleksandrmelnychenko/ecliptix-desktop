using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.AutoUpdater;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;

namespace Ecliptix.Desktop.Integration;

public class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

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
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        ServiceCollection services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Log.Logger);

        UpdateConfiguration updateConfig = configuration.GetSection("UpdateService").Get<UpdateConfiguration>()
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

        services.AddSingleton<UpdateManager>(sp =>
        {
            UpdateConfiguration config = sp.GetRequiredService<UpdateConfiguration>();
            ILogger logger = sp.GetRequiredService<ILogger>();

            Log.Information("Initializing UpdateManager");
            return new UpdateManager(config, logger);
        });

        services.AddTransient<UpdateViewModel>();
        services.AddTransient<MainWindowViewModel>();

        ServiceProvider serviceProvider = services.BuildServiceProvider();

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
                UpdateManager? updateManager = ServiceProvider?.GetService<UpdateManager>();
                if (updateManager == null)
                {
                    Log.Warning("UpdateManager not available");
                }
                else
                {
                    Log.Information("UpdateManager initialized successfully");

                    updateManager.UpdateAvailable += OnUpdateAvailable;
                    updateManager.ErrorOccurred += OnUpdateError;
                }

                MainWindowViewModel mainViewModel = ServiceProvider?.GetService<MainWindowViewModel>()
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

public class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger _logger;
    private UpdateViewModel? _updateViewModel;

    public MainWindowViewModel(UpdateManager updateManager)
    {
        _logger = Log.ForContext<MainWindowViewModel>();
        _logger.Information("MainWindowViewModel initializing");

        UpdateViewModel = new UpdateViewModel(updateManager);

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
