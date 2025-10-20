# Ecliptix.AutoUpdater

Auto-update library for Ecliptix Desktop application.

## Features

- ✅ Cross-platform support (Windows, macOS, Linux)
- ✅ Background update checking
- ✅ Progress tracking for downloads
- ✅ SHA-256 checksum verification
- ✅ Critical/mandatory updates
- ✅ Minimum version enforcement
- ✅ Release notes display
- ✅ Silent updates option

## Integration

### 1. Add Project Reference

Add to `Ecliptix.Core.Desktop.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\..\Ecliptix.AutoUpdater\Ecliptix.AutoUpdater.csproj" />
</ItemGroup>
```

### 2. Initialize Update Service

```csharp
using Ecliptix.AutoUpdater;

// In your application startup
var updateService = new UpdateService(
    updateServerUrl: "https://updates.ecliptix.com",
    currentVersion: "1.0.0"
);

// Subscribe to download progress
updateService.DownloadProgressChanged += (sender, progress) =>
{
    Console.WriteLine($"Download: {progress.Percentage}% - {progress.StatusMessage}");
};
```

### 3. Check for Updates

```csharp
// Check for updates
var result = await updateService.CheckForUpdatesAsync();

if (result.IsUpdateAvailable)
{
    Console.WriteLine($"Update available: {result.LatestVersion}");
    Console.WriteLine($"Current version: {result.CurrentVersion}");
    Console.WriteLine($"Critical: {result.IsCritical}");

    if (result.Manifest != null)
    {
        Console.WriteLine($"Release notes:\n{result.Manifest.ReleaseNotes}");
    }
}
else
{
    Console.WriteLine("No updates available");
}
```

### 4. Download and Install Update

```csharp
if (result.IsUpdateAvailable && result.Manifest != null)
{
    var success = await updateService.DownloadAndInstallUpdateAsync(result.Manifest);

    if (success)
    {
        Console.WriteLine("Update installed successfully");
        // Application will exit and installer will launch
    }
    else
    {
        Console.WriteLine("Update installation failed");
    }
}
```

## UI Integration Examples

### Avalonia UI (MVVM)

```csharp
public class MainWindowViewModel : ViewModelBase
{
    private readonly UpdateService _updateService;
    private bool _isUpdateAvailable;
    private string _updateMessage = string.Empty;
    private int _downloadProgress;

    public MainWindowViewModel()
    {
        _updateService = new UpdateService(
            "https://updates.ecliptix.com",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
        );

        _updateService.DownloadProgressChanged += OnDownloadProgressChanged;

        // Check for updates on startup
        _ = CheckForUpdatesAsync();
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set => this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
    }

    public string UpdateMessage
    {
        get => _updateMessage;
        set => this.RaiseAndSetIfChanged(ref _updateMessage, value);
    }

    public int DownloadProgress
    {
        get => _downloadProgress;
        set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }

    public ReactiveCommand<Unit, Unit> InstallUpdateCommand { get; }

    private async Task CheckForUpdatesAsync()
    {
        var result = await _updateService.CheckForUpdatesAsync();

        if (result.IsUpdateAvailable)
        {
            IsUpdateAvailable = true;
            UpdateMessage = $"Version {result.LatestVersion} is available!";

            if (result.IsCritical)
            {
                UpdateMessage = "Critical update required!";
                // Auto-download critical updates
                await InstallUpdateAsync();
            }
        }
    }

    private async Task InstallUpdateAsync()
    {
        var result = await _updateService.CheckForUpdatesAsync();
        if (result.Manifest != null)
        {
            await _updateService.DownloadAndInstallUpdateAsync(result.Manifest);
        }
    }

    private void OnDownloadProgressChanged(object? sender, UpdateProgress progress)
    {
        DownloadProgress = progress.Percentage;
        UpdateMessage = progress.StatusMessage;
    }
}
```

### Avalonia XAML

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <DockPanel>
        <!-- Update notification banner -->
        <Border DockPanel.Dock="Top"
                Background="#FFF3CD"
                Padding="10"
                IsVisible="{Binding IsUpdateAvailable}">
            <StackPanel Orientation="Horizontal" Spacing="10">
                <TextBlock Text="🔔" FontSize="16" />
                <TextBlock Text="{Binding UpdateMessage}"
                          VerticalAlignment="Center" />
                <Button Content="Update Now"
                        Command="{Binding InstallUpdateCommand}" />
            </StackPanel>
        </Border>

        <!-- Download progress -->
        <ProgressBar DockPanel.Dock="Top"
                     Value="{Binding DownloadProgress}"
                     IsVisible="{Binding DownloadProgress,
                                Converter={StaticResource IsGreaterThanZero}}"
                     Height="4" />

        <!-- Rest of your UI -->
        <ContentControl Content="{Binding CurrentPage}" />
    </DockPanel>
</Window>
```

## Background Update Checking

```csharp
public class UpdateChecker
{
    private readonly UpdateService _updateService;
    private readonly Timer _timer;

    public UpdateChecker(UpdateService updateService)
    {
        _updateService = updateService;

        // Check for updates every 6 hours
        _timer = new Timer(CheckForUpdates, null, TimeSpan.Zero, TimeSpan.FromHours(6));
    }

    private async void CheckForUpdates(object? state)
    {
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();

            if (result.IsUpdateAvailable)
            {
                // Notify user (toast notification, dialog, etc.)
                await NotifyUserOfUpdateAsync(result);
            }
        }
        catch (Exception ex)
        {
            // Log error but don't disturb user
            Debug.WriteLine($"Background update check failed: {ex.Message}");
        }
    }

    private async Task NotifyUserOfUpdateAsync(UpdateCheckResult result)
    {
        // Show notification based on platform
        // Windows: Toast notification
        // macOS: User notification
        // Linux: Desktop notification
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
```

## Configuration

### appsettings.json

```json
{
  "UpdateService": {
    "UpdateServerUrl": "https://updates.ecliptix.com",
    "CheckInterval": "06:00:00",
    "EnableAutoCheck": true,
    "EnableAutoDownload": false,
    "NotifyOnUpdateAvailable": true
  }
}
```

### Loading Configuration

```csharp
public class UpdateConfiguration
{
    public string UpdateServerUrl { get; set; } = string.Empty;
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);
    public bool EnableAutoCheck { get; set; } = true;
    public bool EnableAutoDownload { get; set; } = false;
    public bool NotifyOnUpdateAvailable { get; set; } = true;
}

// In Program.cs or startup
var config = configuration.GetSection("UpdateService").Get<UpdateConfiguration>();
var updateService = new UpdateService(config.UpdateServerUrl, currentVersion);
```

## Testing

### Mock Update Server

For development/testing:

```csharp
// Use local test server
var updateService = new UpdateService(
    "http://localhost:8080",
    "1.0.0"
);
```

### Test Manifest

Create a test `manifest.json` with a higher version number:

```json
{
  "version": "999.0.0",
  "releaseDate": "2025-01-15T00:00:00Z",
  "releaseNotes": "Test update",
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

## Security

### HTTPS Only

Always use HTTPS in production:

```csharp
var updateService = new UpdateService(
    "https://updates.ecliptix.com",  // ✅ HTTPS
    currentVersion
);

// NOT:
var updateService = new UpdateService(
    "http://updates.ecliptix.com",   // ❌ HTTP
    currentVersion
);
```

### Checksum Verification

The library automatically verifies SHA-256 checksums. Never disable this check.

### Certificate Pinning (Optional)

For extra security, implement certificate pinning:

```csharp
// Use your existing certificate pinning infrastructure
var httpClient = new HttpClient(new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
    {
        // Your certificate pinning logic
        return CertificatePinningService.ValidateCertificate(cert);
    }
});
```

## Troubleshooting

### Update Check Fails

- Verify update server is accessible
- Check manifest.json is valid JSON
- Confirm HTTPS certificate is valid

### Download Fails

- Check network connectivity
- Verify download URL is accessible
- Check available disk space

### Installation Fails

- Verify checksum matches
- Check installer file permissions
- Ensure sufficient privileges (Windows UAC)

### Version Comparison Issues

Versions must follow semantic versioning: `major.minor.patch`

✅ Valid: `1.0.0`, `1.2.3`, `2.0.0`
❌ Invalid: `1.0`, `v1.0.0`, `1.0.0-beta`

## API Reference

See inline XML documentation for detailed API reference.

## License

Part of Ecliptix Desktop - All rights reserved.
