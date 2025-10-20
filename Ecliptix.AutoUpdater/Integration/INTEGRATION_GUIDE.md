# Production Auto-Updater Integration Guide

This guide shows how to integrate the production-ready auto-updater system into your Ecliptix Desktop application.

## Quick Start

### 1. Add Configuration

Edit `appsettings.json`:

```json
{
  "UpdateService": {
    "UpdateServerUrl": "https://updates.ecliptix.com",
    "CheckInterval": "06:00:00",
    "EnableAutoCheck": true,
    "EnableAutoDownload": false,
    "NotifyOnUpdateAvailable": true,
    "AutoRestartAfterUpdate": false,
    "UpdateChannel": "stable"
  }
}
```

### 2. Register Services in Program.cs

```csharp
using Ecliptix.AutoUpdater;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = BuildAvaloniaApp();

        // Build and start the app
        builder.StartWithClassicDesktopLifetime(args);
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

        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            // Add Serilog if you're using it
            // builder.AddSerilog();
        });

        // Register update configuration
        var updateConfig = configuration.GetSection("UpdateService").Get<UpdateConfiguration>()
            ?? new UpdateConfiguration();
        services.AddSingleton(updateConfig);

        // Register update manager as singleton
        services.AddSingleton<UpdateManager>();

        // Register update view model
        services.AddTransient<UpdateViewModel>();

        // Build service provider
        var serviceProvider = services.BuildServiceProvider();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .With(serviceProvider); // Make service provider available
    }
}
```

### 3. Initialize in App.axaml.cs

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.AutoUpdater;
using Microsoft.Extensions.DependencyInjection;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Get service provider (set in Program.cs)
            _serviceProvider = // Get from somewhere, e.g., a static property

            // Get update manager
            var updateManager = _serviceProvider?.GetService<UpdateManager>();

            // Create main window
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(updateManager)
            };

            // Start automatic update checking
            // This happens automatically if EnableAutoCheck is true
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

### 4. Add to MainWindowViewModel

```csharp
using Ecliptix.AutoUpdater;
using Ecliptix.AutoUpdater.ViewModels;
using ReactiveUI;

public class MainWindowViewModel : ViewModelBase
{
    private readonly UpdateManager _updateManager;
    private UpdateViewModel? _updateViewModel;

    public MainWindowViewModel(UpdateManager updateManager)
    {
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));

        // Create update view model
        UpdateViewModel = new UpdateViewModel(_updateManager);

        // Optionally check for updates on startup
        _ = CheckForUpdatesOnStartupAsync();
    }

    public UpdateViewModel UpdateViewModel
    {
        get => _updateViewModel;
        private set => this.RaiseAndSetIfChanged(ref _updateViewModel, value);
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        // Wait a bit before checking (let app finish loading)
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Check for updates
        await UpdateViewModel.CheckForUpdatesCommand.Execute();
    }
}
```

### 5. Add UI to MainWindow.axaml

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:YourApp.ViewModels"
        xmlns:updateViews="using:Ecliptix.AutoUpdater.Views"
        x:Class="YourApp.Views.MainWindow"
        x:DataType="vm:MainWindowViewModel"
        Title="Ecliptix Desktop"
        Width="1200"
        Height="800">

    <DockPanel>

        <!-- Update notification banner at the top -->
        <updateViews:UpdateNotificationBanner
            DockPanel.Dock="Top"
            DataContext="{Binding UpdateViewModel}"/>

        <!-- Your main content -->
        <ContentControl Content="{Binding CurrentView}"/>

    </DockPanel>

</Window>
```

---

## Advanced Usage

### Manual Update Check

Add a menu item or button to manually check for updates:

```xml
<MenuItem Header="Help">
    <MenuItem Header="Check for Updates..."
             Command="{Binding UpdateViewModel.CheckForUpdatesCommand}"/>
</MenuItem>
```

### Show Update Dialog

```csharp
public async Task ShowUpdateDialogAsync()
{
    var dialog = new UpdateDialog
    {
        DataContext = UpdateViewModel
    };

    await dialog.ShowDialog(MainWindow);
}
```

### Custom Update Notifications

```csharp
public class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(UpdateManager updateManager)
    {
        _updateManager = updateManager;

        // Subscribe to update events
        _updateManager.UpdateAvailable += OnUpdateAvailable;
        _updateManager.ErrorOccurred += OnUpdateError;
    }

    private void OnUpdateAvailable(object? sender, UpdateCheckResult result)
    {
        if (result.IsCriticalUpdate)
        {
            // Show modal dialog for critical updates
            ShowCriticalUpdateDialog(result);
        }
        else
        {
            // Show notification banner for normal updates
            UpdateViewModel.ShowUpdateDialog = true;

            // Or show a toast notification
            ShowToastNotification($"Update {result.LatestVersion} available!");
        }
    }

    private void OnUpdateError(object? sender, string error)
    {
        // Log error or show notification
        _logger.LogError("Update check failed: {Error}", error);
    }
}
```

### Background Updates

For silent background updates:

```json
{
  "UpdateService": {
    "EnableAutoDownload": true,
    "AutoRestartAfterUpdate": false
  }
}
```

Then handle restart on app shutdown:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.ShutdownRequested += OnShutdownRequested;
    }

    base.OnFrameworkInitializationCompleted();
}

private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
{
    // Check if update is downloaded and ready
    var updateInfo = _updateManager.GetCachedUpdateInfo();
    if (updateInfo?.IsUpdateAvailable == true)
    {
        // Ask user if they want to install now
        var result = await ShowUpdateInstallDialog();
        if (result == true)
        {
            // Cancel shutdown to install update
            e.Cancel = true;

            // Install update (will restart app)
            await _updateManager.DownloadAndInstallUpdateAsync();
        }
    }
}
```

---

## Configuration Options

### UpdateConfiguration Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `UpdateServerUrl` | string | "" | URL of your update server |
| `CheckInterval` | TimeSpan | 6 hours | How often to check for updates |
| `EnableAutoCheck` | bool | true | Automatically check for updates |
| `EnableAutoDownload` | bool | false | Automatically download updates |
| `NotifyOnUpdateAvailable` | bool | true | Show notification when update found |
| `AutoRestartAfterUpdate` | bool | false | Auto-restart after installing |
| `UpdateChannel` | string | "stable" | Update channel (stable/beta/canary) |

---

## Testing

### Local Testing

1. Set up a local test server:

```bash
# Install a simple HTTP server
npm install -g http-server

# Serve test manifest
cd UpdateServer
http-server -p 8080 --cors
```

2. Update `appsettings.Development.json`:

```json
{
  "UpdateService": {
    "UpdateServerUrl": "http://localhost:8080",
    "CheckInterval": "00:01:00"  // Check every minute for testing
  }
}
```

3. Create a test manifest with higher version:

```json
{
  "version": "999.0.0",
  "releaseDate": "2025-01-20T00:00:00Z",
  "releaseNotes": "### Test Update\n\n- This is a test update\n- For development only",
  "isCritical": false,
  "platforms": {
    "win-x64": {
      "downloadUrl": "http://localhost:8080/test-installer.exe",
      "fileSize": 1024,
      "sha256": "test-hash",
      "installerType": "exe"
    }
  }
}
```

---

## Production Deployment

### Checklist

- [ ] Update server is running and accessible via HTTPS
- [ ] SSL certificate is valid
- [ ] manifest.json is properly formatted
- [ ] All download URLs are accessible
- [ ] SHA-256 checksums are correct
- [ ] Code signing certificates are applied
- [ ] Version numbers are incremented
- [ ] Release notes are written
- [ ] Testing completed on all platforms
- [ ] Rollback plan is ready

### Monitoring

Monitor these metrics:

- Update check success rate
- Download completion rate
- Installation success rate
- Time to update (from check to install)
- Error rates by version

Use your logging infrastructure to track:

```csharp
_logger.LogInformation(
    "Update check completed. Available: {IsAvailable}, Version: {Version}",
    result.IsUpdateAvailable,
    result.LatestVersion
);
```

---

## Troubleshooting

### Update checks fail

- Verify `UpdateServerUrl` is correct
- Check network connectivity
- Ensure HTTPS certificate is valid
- Check firewall settings

### Updates not detected

- Verify version comparison logic
- Check manifest.json format
- Ensure version numbers are semantic (major.minor.patch)

### Download fails

- Check download URL is accessible
- Verify file permissions
- Check available disk space
- Review SHA-256 checksum

### Installation fails

- Check installer file integrity
- Verify user has sufficient permissions
- Review installer logs
- Ensure no antivirus blocking

---

## Security Considerations

1. **Always use HTTPS** for update server
2. **Verify SHA-256 checksums** before installation
3. **Code sign all installers** with valid certificates
4. **Use certificate pinning** if extra security needed
5. **Never disable signature verification**
6. **Keep update server secure** and monitored
7. **Have a rollback plan** for bad updates

---

## Support

For issues with the auto-updater:

1. Check application logs
2. Verify configuration
3. Test with development manifest
4. Review this integration guide
5. Check [ROADMAP-INSTALLERS.md](../../../ROADMAP-INSTALLERS.md) for enhancement ideas

---

Happy updating! 🚀
