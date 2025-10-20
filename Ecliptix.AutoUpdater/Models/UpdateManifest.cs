using System.Text.Json.Serialization;

namespace Ecliptix.AutoUpdater.Models;

public class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("releaseDate")]
    public DateTime ReleaseDate { get; set; }

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = string.Empty;

    [JsonPropertyName("isCritical")]
    public bool IsCritical { get; set; }

    [JsonPropertyName("minimumVersion")]
    public string? MinimumVersion { get; set; }

    [JsonPropertyName("platforms")]
    public Dictionary<string, PlatformUpdate> Platforms { get; set; } = new();
}

public class PlatformUpdate
{
    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("installerType")]
    public string InstallerType { get; set; } = string.Empty;
}

public class UpdateCheckResult
{
    public bool IsUpdateAvailable { get; set; }
    public bool IsCritical { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public UpdateManifest? Manifest { get; set; }
    public string? ErrorMessage { get; set; }
}

public class UpdateProgress
{
    public long BytesDownloaded { get; set; }
    public long TotalBytes { get; set; }
    public int Percentage => TotalBytes > 0 ? (int)((BytesDownloaded * 100) / TotalBytes) : 0;
    public string StatusMessage { get; set; } = string.Empty;
}
