using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ecliptix.AutoUpdater.Models;

namespace Ecliptix.AutoUpdater;

public class UpdateService
{
    private readonly HttpClient _httpClient;
    private readonly string _updateServerUrl;
    private readonly string _currentVersion;
    private readonly string _appDataPath;

    public string CurrentVersion => _currentVersion;

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

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string manifestUrl = $"{_updateServerUrl}/manifest.json";
            HttpResponseMessage response = await _httpClient.GetAsync(manifestUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = $"Failed to fetch update manifest: {response.StatusCode}"
                };
            }

            string manifestJson = await response.Content.ReadAsStringAsync(cancellationToken);
            UpdateManifest? manifest = JsonSerializer.Deserialize(manifestJson, UpdateJsonContext.Default.UpdateManifest);

            if (manifest == null)
            {
                return new UpdateCheckResult
                {
                    IsUpdateAvailable = false,
                    CurrentVersion = _currentVersion,
                    ErrorMessage = "Invalid update manifest"
                };
            }

            bool isUpdateAvailable = CompareVersions(_currentVersion, manifest.Version) < 0;
            bool isCritical = manifest.IsCritical ||
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

    public async Task<bool> DownloadAndInstallUpdateAsync(
        UpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string platform = GetCurrentPlatform();
            if (!manifest.Platforms.TryGetValue(platform, out PlatformUpdate? platformUpdate))
            {
                throw new InvalidOperationException($"No update available for platform: {platform}");
            }

            string downloadPath = await DownloadUpdateAsync(platformUpdate, cancellationToken);

            if (!await VerifyChecksumAsync(downloadPath, platformUpdate.Sha256))
            {
                File.Delete(downloadPath);
                throw new InvalidOperationException("Update file checksum verification failed");
            }

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
        string fileName = Path.GetFileName(new Uri(platformUpdate.DownloadUrl).AbsolutePath);
        string downloadPath = Path.Combine(_appDataPath, fileName);

        if (File.Exists(downloadPath))
        {
            File.Delete(downloadPath);
        }

        using HttpResponseMessage response = await _httpClient.GetAsync(
            platformUpdate.DownloadUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? platformUpdate.FileSize;
        long bytesDownloaded = 0L;

        using Stream contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using FileStream fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[8192];
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            bytesDownloaded += bytesRead;

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
        using SHA256 sha256 = SHA256.Create();
        using FileStream stream = File.OpenRead(filePath);
        byte[] hashBytes = await sha256.ComputeHashAsync(stream);
        string actualHash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
        return actualHash.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InstallUpdateAsync(string installerPath, string installerType)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
                Arguments = "/SILENT /NORESTART"
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
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
        version1 = version1.TrimStart('v').Split('-')[0];
        version2 = version2.TrimStart('v').Split('-')[0];

        int[] v1Parts = version1.Split('.').Select(int.Parse).ToArray();
        int[] v2Parts = version2.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Max(v1Parts.Length, v2Parts.Length); i++)
        {
            int v1Part = i < v1Parts.Length ? v1Parts[i] : 0;
            int v2Part = i < v2Parts.Length ? v2Parts[i] : 0;

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
