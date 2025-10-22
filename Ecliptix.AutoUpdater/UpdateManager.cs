using System.Diagnostics;
using System.Reflection;
using Ecliptix.AutoUpdater.Models;
using Microsoft.Extensions.Logging;

namespace Ecliptix.AutoUpdater;

public class UpdateManager : IDisposable
{
    private readonly UpdateService _updateService;
    private readonly ILogger? _logger;
    private readonly Timer _checkTimer;
    private readonly UpdateConfiguration _config;
    private UpdateCheckResult? _lastCheckResult;
    private bool _isChecking;
    private bool _isDownloading;

    public event EventHandler<UpdateCheckResult>? UpdateAvailable;
    public event EventHandler<UpdateProgress>? DownloadProgressChanged;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsCheckingForUpdates => _isChecking;
    public bool IsDownloading => _isDownloading;
    public UpdateCheckResult? LastCheckResult => _lastCheckResult;

    public UpdateManager(
        UpdateConfiguration config,
        ILogger? logger = null)
    {
        _config = config;
        _logger = logger;

        string version = Assembly.GetEntryAssembly()?
            .GetName()
            .Version?
            .ToString() ?? "1.0.0";

        _updateService = new UpdateService(config.UpdateServerUrl, version);
        _updateService.DownloadProgressChanged += OnDownloadProgressChanged;

        if (config.EnableAutoCheck && config.CheckInterval > TimeSpan.Zero)
        {
            _checkTimer = new Timer(
                async _ => await CheckForUpdatesInternalAsync(),
                null,
                TimeSpan.FromSeconds(30),
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
                CurrentVersion = _updateService.CurrentVersion,
                IsUpdateAvailable = false,
                ErrorMessage = "Check already in progress"
            };
        }

        try
        {
            _isChecking = true;
            _logger?.LogInformation("Checking for updates...");

            UpdateCheckResult result = await _updateService.CheckForUpdatesAsync(cancellationToken);

            _lastCheckResult = result;

            if (result.IsUpdateAvailable)
            {
                _logger?.LogInformation(
                    "Update available: {CurrentVersion} -> {LatestVersion}",
                    result.CurrentVersion,
                    result.LatestVersion);

                UpdateAvailable?.Invoke(this, result);

                if (_config.EnableAutoDownload && !result.IsCritical)
                {
                    _logger?.LogInformation("Auto-download enabled, starting download...");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
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

            UpdateCheckResult errorResult = new UpdateCheckResult
            {
                CurrentVersion = _updateService.CurrentVersion,
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

            bool success = await _updateService.DownloadAndInstallUpdateAsync(
                _lastCheckResult.Manifest,
                cancellationToken);

            if (success)
            {
                _logger?.LogInformation("Update downloaded and ready to install");

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
            string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
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

public class UpdateConfiguration
{
    public string UpdateServerUrl { get; set; } = string.Empty;
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(6);
    public bool EnableAutoCheck { get; set; } = true;
    public bool EnableAutoDownload { get; set; } = false;
    public bool NotifyOnUpdateAvailable { get; set; } = true;
    public bool AutoRestartAfterUpdate { get; set; } = false;
    public string UpdateChannel { get; set; } = "stable";
}
