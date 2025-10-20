using System.Diagnostics;
using System.Reflection;
using Ecliptix.AutoUpdater.Models;
using Serilog;

namespace Ecliptix.AutoUpdater;

/// <summary>
/// Production-ready update manager with background checking and error handling
/// </summary>
public class UpdateManager : IDisposable
{
    private readonly UpdateService _updateService;
    private readonly ILogger? _logger;
    private readonly Timer _checkTimer;
    private readonly UpdateConfiguration _config;
    private UpdateCheckResult? _lastCheckResult;
    private bool _isChecking;
    private bool _isDownloading;

    /// <summary>
    /// Raised when update availability changes
    /// </summary>
    public event EventHandler<UpdateCheckResult>? UpdateAvailable;

    /// <summary>
    /// Raised when download progress changes
    /// </summary>
    public event EventHandler<UpdateProgress>? DownloadProgressChanged;

    /// <summary>
    /// Raised when an error occurs
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// Gets whether an update is currently being checked
    /// </summary>
    public bool IsCheckingForUpdates => _isChecking;

    /// <summary>
    /// Gets whether an update is currently downloading
    /// </summary>
    public bool IsDownloading => _isDownloading;

    /// <summary>
    /// Gets the last check result
    /// </summary>
    public UpdateCheckResult? LastCheckResult => _lastCheckResult;

    public UpdateManager(
        UpdateConfiguration config,
        ILogger? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;

        // Get current version
        var version = Assembly.GetEntryAssembly()?
            .GetName()
            .Version?
            .ToString() ?? "1.0.0";

        _updateService = new UpdateService(config.UpdateServerUrl, version);
        _updateService.DownloadProgressChanged += OnDownloadProgressChanged;

        // Set up automatic checking if enabled
        if (config.EnableAutoCheck && config.CheckInterval > TimeSpan.Zero)
        {
            _checkTimer = new Timer(
                async _ => await CheckForUpdatesInternalAsync(),
                null,
                TimeSpan.FromSeconds(30), // Check 30 seconds after startup
                config.CheckInterval
            );

            _logger?.LogInformation(
                "Automatic update checking enabled. Interval: {Interval}",
                config.CheckInterval);
        }
        else
        {
            _checkTimer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>
    /// Check for updates manually
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        return await CheckForUpdatesInternalAsync(cancellationToken);
    }

    private async Task<UpdateCheckResult> CheckForUpdatesInternalAsync(CancellationToken cancellationToken = default)
    {
        if (_isChecking)
        {
            _logger?.LogDebug("Update check already in progress");
            return _lastCheckResult ?? new UpdateCheckResult
            {
                CurrentVersion = _updateService._currentVersion,
                IsUpdateAvailable = false,
                ErrorMessage = "Check already in progress"
            };
        }

        try
        {
            _isChecking = true;
            _logger?.LogInformation("Checking for updates...");

            var result = await _updateService.CheckForUpdatesAsync(cancellationToken);

            _lastCheckResult = result;

            if (result.IsUpdateAvailable)
            {
                _logger?.LogInformation(
                    "Update available: {CurrentVersion} -> {LatestVersion}",
                    result.CurrentVersion,
                    result.LatestVersion);

                UpdateAvailable?.Invoke(this, result);

                // Auto-download if enabled and not critical (critical updates should prompt user)
                if (_config.EnableAutoDownload && !result.IsCritical)
                {
                    _logger?.LogInformation("Auto-download enabled, starting download...");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5)); // Small delay
                        await DownloadAndInstallUpdateAsync(cancellationToken);
                    });
                }
            }
            else
            {
                _logger?.LogInformation("No updates available");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking for updates");

            var errorResult = new UpdateCheckResult
            {
                CurrentVersion = _updateService._currentVersion,
                IsUpdateAvailable = false,
                ErrorMessage = ex.Message
            };

            ErrorOccurred?.Invoke(this, ex.Message);

            return errorResult;
        }
        finally
        {
            _isChecking = false;
        }
    }

    /// <summary>
    /// Download and install the latest update
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_isDownloading)
        {
            _logger?.LogWarning("Download already in progress");
            return false;
        }

        if (_lastCheckResult?.Manifest == null)
        {
            _logger?.LogWarning("No update manifest available. Check for updates first.");
            return false;
        }

        try
        {
            _isDownloading = true;
            _logger?.LogInformation("Starting update download and installation...");

            var success = await _updateService.DownloadAndInstallUpdateAsync(
                _lastCheckResult.Manifest,
                cancellationToken);

            if (success)
            {
                _logger?.LogInformation("Update downloaded and ready to install");

                // Notify user that app will restart
                if (_config.AutoRestartAfterUpdate)
                {
                    _logger?.LogInformation("Auto-restart enabled, application will restart in 3 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                    RestartApplication();
                }
            }
            else
            {
                _logger?.LogWarning("Update installation failed");
                ErrorOccurred?.Invoke(this, "Update installation failed");
            }

            return success;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Update download cancelled");
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error downloading/installing update");
            ErrorOccurred?.Invoke(this, $"Update failed: {ex.Message}");
            return false;
        }
        finally
        {
            _isDownloading = false;
        }
    }

    /// <summary>
    /// Get update information without checking server (uses cached result)
    /// </summary>
    public UpdateCheckResult? GetCachedUpdateInfo()
    {
        return _lastCheckResult;
    }

    private void OnDownloadProgressChanged(object? sender, UpdateProgress progress)
    {
        DownloadProgressChanged?.Invoke(this, progress);

        _logger?.LogDebug(
            "Download progress: {Percentage}% ({Downloaded}/{Total} bytes)",
            progress.Percentage,
            progress.BytesDownloaded,
            progress.TotalBytes);
    }

    private void RestartApplication()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (exePath != null)
            {
                Process.Start(exePath);
            }

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error restarting application");
        }
    }

    public void Dispose()
    {
        _checkTimer?.Dispose();
        _updateService?.Dispose();
    }
}

/// <summary>
/// Configuration for update manager
/// </summary>
public class UpdateConfiguration
{
    /// <summary>
    /// URL of the update server
    /// </summary>
    public string UpdateServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// How often to check for updates
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Enable automatic update checking
    /// </summary>
    public bool EnableAutoCheck { get; set; } = true;

    /// <summary>
    /// Automatically download updates when available
    /// </summary>
    public bool EnableAutoDownload { get; set; } = false;

    /// <summary>
    /// Show notification when update is available
    /// </summary>
    public bool NotifyOnUpdateAvailable { get; set; } = true;

    /// <summary>
    /// Automatically restart application after update installation
    /// </summary>
    public bool AutoRestartAfterUpdate { get; set; } = false;

    /// <summary>
    /// Update channel (stable, beta, canary)
    /// </summary>
    public string UpdateChannel { get; set; } = "stable";
}
