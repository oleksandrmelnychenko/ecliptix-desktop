using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ecliptix.AutoUpdater.Models;

namespace Ecliptix.AutoUpdater;

/// <summary>
/// Service for checking and applying application updates
/// </summary>
public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _updateServerUrl;
    private readonly string _currentVersion;
    private readonly string _appDataPath;

    /// <summary>
    /// Event raised when download progress changes
    /// </summary>
    public event EventHandler<UpdateProgress>? DownloadProgressChanged;

    public UpdateService(string updateServerUrl, string currentVersion)
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _updateServerUrl = updateServerUrl.TrimEnd('/');
        _currentVersion = currentVersion;
        _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Ecliptix",
            "Updates"
        );

        Directory.CreateDirectory(_appDataPath);
    }

    /// <summary>
    /// Check if an update is available
    /// </summary>
    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manifestUrl = $"{_updateServerUrl}/manifest.json";
            var response = await _httpClient.GetAsync(manifestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = $"Failed to fetch update manifest: {response.StatusCode}"
                };
            }

            var manifestJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson);

            if (manifest == null)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = "Invalid update manifest"
                };
            }

            // Compare versions
            var isUpdateAvailable = CompareVersions(_currentVersion, manifest.Version) < 0;
            var isCritical = manifest.IsCritical ||
                           (manifest.MinimumVersion != null &&
                            CompareVersions(_currentVersion, manifest.MinimumVersion) < 0);

            return new UpdateCheckResult
            {
                IsUpdateAvailable = isUpdateAvailable,
                IsCritical = isCritical,
                CurrentVersion = _currentVersion,
                LatestVersion = manifest.Version,
                Manifest = manifest
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult
            {
                IsUpdateAvailable = false,
                CurrentVersion = _currentVersion,
                ErrorMessage = $"Error checking for updates: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Download and install an update
    /// </summary>
    public async Task<bool> DownloadAndInstallUpdateAsync(
        UpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var platform = GetCurrentPlatform();
            if (!manifest.Platforms.TryGetValue(platform, out var platformUpdate))
            {
                throw new InvalidOperationException($"No update available for platform: {platform}");
            }

            // Download the update
            var downloadPath = await DownloadUpdateAsync(platformUpdate, cancellationToken);

            // Verify checksum
            if (!await VerifyChecksumAsync(downloadPath, platformUpdate.Sha256))
            {
                File.Delete(downloadPath);
                throw new InvalidOperationException("Update file checksum verification failed");
            }

            // Install the update
            await InstallUpdateAsync(downloadPath, platformUpdate.InstallerType);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error downloading/installing update: {ex.Message}");
            return false;
        }
    }

    private async Task<string> DownloadUpdateAsync(
        PlatformUpdate platformUpdate,
        CancellationToken cancellationToken)
    {
        var fileName = Path.GetFileName(new Uri(platformUpdate.DownloadUrl).AbsolutePath);
        var downloadPath = Path.Combine(_appDataPath, fileName);

        // Delete existing file if present
        if (File.Exists(downloadPath))
        {
            File.Delete(downloadPath);
        }

        using var response = await _httpClient.GetAsync(
            platformUpdate.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? platformUpdate.FileSize;
        var bytesDownloaded = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesDownloaded += bytesRead;

            // Report progress
            DownloadProgressChanged?.Invoke(this, new UpdateProgress
            {
                BytesDownloaded = bytesDownloaded,
                TotalBytes = totalBytes,
                StatusMessage = "Downloading update..."
            });
        }

        return downloadPath;
    }

    private async Task<bool> VerifyChecksumAsync(string filePath, string expectedSha256)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hashBytes = await sha256.ComputeHashAsync(stream);
        var actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        return actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallUpdateAsync(string installerPath, string installerType)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Launch installer and exit application
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Arguments = "/SILENT /NORESTART"
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Open DMG for user to drag and drop
            Process.Start(new ProcessStartInfo
            {
                FileName = "open",
                Arguments = installerPath,
                UseShellExecute = true
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (installerType == "appimage")
            {
                // Make AppImage executable and show in file manager
                File.SetUnixFileMode(installerPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = Path.GetDirectoryName(installerPath),
                    UseShellExecute = true
                });
            }
            else if (installerType == "deb" || installerType == "rpm")
            {
                // Show installer in file manager for manual installation
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = Path.GetDirectoryName(installerPath),
                    UseShellExecute = true
                });
            }
        }

        await Task.CompletedTask;
    }

    private static string GetCurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
        }

        throw new PlatformNotSupportedException("Unsupported platform");
    }

    private static int CompareVersions(string version1, string version2)
    {
        // Remove 'v' prefix if present and any build metadata
        version1 = version1.TrimStart('v').Split('-')[0];
        version2 = version2.TrimStart('v').Split('-')[0];

        var v1Parts = version1.Split('.').Select(int.Parse).ToArray();
        var v2Parts = version2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(v1Parts.Length, v2Parts.Length); i++)
        {
            var v1Part = i < v1Parts.Length ? v1Parts[i] : 0;
            var v2Part = i < v2Parts.Length ? v2Parts[i] : 0;

            if (v1Part < v2Part) return -1;
            if (v1Part > v2Part) return 1;
        }

        return 0;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
