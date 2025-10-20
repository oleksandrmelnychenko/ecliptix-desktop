using System.Text.Json.Serialization;

namespace Ecliptix.AutoUpdater.Models;

/// <summary>
/// Represents the update manifest containing information about available updates
/// </summary>
public class UpdateManifest
{
    /// <summary>
    /// Latest available version
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Release date in ISO 8601 format
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public DateTime ReleaseDate { get; set; }

    /// <summary>
    /// Release notes for this version
    /// </summary>
    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    /// <summary>
    /// Is this a critical/mandatory update
    /// </summary>
    [JsonPropertyName("isCritical")]
    public bool IsCritical { get; set; }

    /// <summary>
    /// Minimum supported version (versions below this must update)
    /// </summary>
    [JsonPropertyName("minimumVersion")]
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Platform-specific download information
    /// </summary>
    [JsonPropertyName("platforms")]
    public Dictionary<string, PlatformUpdate> Platforms { get; set; } = new();
}

/// <summary>
/// Platform-specific update information
/// </summary>
public class PlatformUpdate
{
    /// <summary>
    /// Download URL for this platform
    /// </summary>
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// File size in bytes
    /// </summary>
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    /// <summary>
    /// SHA-256 checksum for verification
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>
    /// Installer type (exe, dmg, deb, rpm, appimage)
    /// </summary>
    [JsonPropertyName("installerType")]
    public string InstallerType { get; set; } = string.Empty;
}

/// <summary>
/// Result of an update check
/// </summary>
public class UpdateCheckResult
{
    /// <summary>
    /// Is an update available
    /// </summary>
    public bool IsUpdateAvailable { get; set; }

    /// <summary>
    /// Is this a critical update
    /// </summary>
    public bool IsCritical { get; set; }

    /// <summary>
    /// Current installed version
    /// </summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>
    /// Latest available version
    /// </summary>
    public string? LatestVersion { get; set; }

    /// <summary>
    /// Update manifest if update is available
    /// </summary>
    public UpdateManifest? Manifest { get; set; }

    /// <summary>
    /// Error message if check failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Progress information for update download
/// </summary>
public class UpdateProgress
{
    /// <summary>
    /// Bytes downloaded
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Total bytes to download
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Download percentage (0-100)
    /// </summary>
    public int Percentage => TotalBytes > 0 ? (int)((BytesDownloaded * 100) / TotalBytes) : 0;

    /// <summary>
    /// Current status message
    /// </summary>
    public string StatusMessage { get; set; } = string.Empty;
}
