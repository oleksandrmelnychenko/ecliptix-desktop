using System.Reactive;
using System.Windows.Input;
using Ecliptix.AutoUpdater.Models;
using ReactiveUI;

namespace Ecliptix.AutoUpdater.ViewModels;

public class UpdateViewModel : ReactiveObject, IDisposable
{
    private readonly UpdateManager _updateManager;
    private bool _isUpdateAvailable;
    private bool _isChecking;
    private bool _isDownloading;
    private int _downloadProgress;
    private string _currentVersion = string.Empty;
    private string _latestVersion = string.Empty;
    private string _releaseNotes = string.Empty;
    private bool _isCriticalUpdate;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _showUpdateDialog;

    public UpdateViewModel(UpdateManager updateManager)
    {
        _updateManager = updateManager;

        _updateManager.UpdateAvailable += OnUpdateAvailable;
        _updateManager.DownloadProgressChanged += OnDownloadProgressChanged;
        _updateManager.ErrorOccurred += OnErrorOccurred;

        UpdateCheckResult? cachedInfo = _updateManager.GetCachedUpdateInfo();
        if (cachedInfo != null)
        {
            UpdateFromCheckResult(cachedInfo);
        }

        CurrentVersion = _updateManager.LastCheckResult?.CurrentVersion ?? "Unknown";

        CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
        InstallUpdateCommand = ReactiveCommand.CreateFromTask(
            InstallUpdateAsync,
            this.WhenAnyValue(x => x.IsUpdateAvailable, x => x.IsDownloading,
                (available, downloading) => available && !downloading));
        DismissUpdateCommand = ReactiveCommand.Create(DismissUpdate);
        ViewReleaseNotesCommand = ReactiveCommand.Create(ViewReleaseNotes);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => this.RaiseAndSetIfChanged(ref _isChecking, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set => this.RaiseAndSetIfChanged(ref _isDownloading, value);
    }

    public int DownloadProgress
    {
        get => _downloadProgress;
        private set => this.RaiseAndSetIfChanged(ref _downloadProgress, value);
    }

    public string CurrentVersion
    {
        get => _currentVersion;
        private set => this.RaiseAndSetIfChanged(ref _currentVersion, value);
    }

    public string LatestVersion
    {
        get => _latestVersion;
        private set => this.RaiseAndSetIfChanged(ref _latestVersion, value);
    }

    public string ReleaseNotes
    {
        get => _releaseNotes;
        private set => this.RaiseAndSetIfChanged(ref _releaseNotes, value);
    }

    public bool IsCriticalUpdate
    {
        get => _isCriticalUpdate;
        private set => this.RaiseAndSetIfChanged(ref _isCriticalUpdate, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool ShowUpdateDialog
    {
        get => _showUpdateDialog;
        set => this.RaiseAndSetIfChanged(ref _showUpdateDialog, value);
    }

    public ICommand CheckForUpdatesCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand DismissUpdateCommand { get; }
    public ICommand ViewReleaseNotesCommand { get; }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            IsChecking = true;
            StatusMessage = "Checking for updates...";
            ErrorMessage = string.Empty;

            UpdateCheckResult result = await _updateManager.CheckForUpdatesAsync();

            UpdateFromCheckResult(result);

            if (!result.IsUpdateAvailable)
            {
                StatusMessage = "You're up to date!";
            }
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task InstallUpdateAsync()
    {
        try
        {
            IsDownloading = true;
            StatusMessage = "Downloading update...";
            ErrorMessage = string.Empty;

            bool success = await _updateManager.DownloadAndInstallUpdateAsync();

            if (success)
            {
                StatusMessage = "Update ready to install. Application will restart...";
            }
            else
            {
                StatusMessage = "Update installation failed";
            }
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private void DismissUpdate()
    {
        ShowUpdateDialog = false;
        StatusMessage = string.Empty;
    }

    private void ViewReleaseNotes()
    {
        if (!string.IsNullOrEmpty(ReleaseNotes))
        {
        }
    }

    private void OnUpdateAvailable(object? sender, UpdateCheckResult result)
    {
        UpdateFromCheckResult(result);

        if (result.IsUpdateAvailable)
        {
            ShowUpdateDialog = true;

            if (result.IsCritical)
            {
                StatusMessage = "Critical update required!";
            }
            else
            {
                StatusMessage = $"Version {result.LatestVersion} is available";
            }
        }
    }

    private void OnDownloadProgressChanged(object? sender, UpdateProgress progress)
    {
        DownloadProgress = progress.Percentage;
        StatusMessage = $"{progress.StatusMessage} ({progress.Percentage}%)";
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        ErrorMessage = error;
        StatusMessage = "An error occurred";
    }

    private void UpdateFromCheckResult(UpdateCheckResult result)
    {
        IsUpdateAvailable = result.IsUpdateAvailable;
        IsCriticalUpdate = result.IsCritical;
        CurrentVersion = result.CurrentVersion;
        LatestVersion = result.LatestVersion ?? string.Empty;
        ReleaseNotes = result.Manifest?.ReleaseNotes ?? string.Empty;

        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            ErrorMessage = result.ErrorMessage;
        }
    }

    public void Dispose()
    {
        _updateManager.UpdateAvailable -= OnUpdateAvailable;
        _updateManager.DownloadProgressChanged -= OnDownloadProgressChanged;
        _updateManager.ErrorOccurred -= OnErrorOccurred;
    }
}
