using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecliptix.Core;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BuildInfo))]
public partial class VersionJsonContext : JsonSerializerContext;

public static class VersionHelper
{
    private static readonly Lazy<string> ApplicationVersion = new(CalculateApplicationVersion);
    private static readonly Lazy<string> InformationalVersion = new(CalculateInformationalVersion);
    private static readonly Lazy<BuildInfo?> BuildInfo = new(LoadBuildInfo);
    private static readonly Lazy<string> DisplayVersion = new(CalculateDisplayVersion);

    public static string GetApplicationVersion() => ApplicationVersion.Value;

    public static string GetInformationalVersion() => InformationalVersion.Value;

    public static BuildInfo? GetBuildInfo() => BuildInfo.Value;

    public static string GetDisplayVersion() => DisplayVersion.Value;

    private static string CalculateApplicationVersion()
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version
                          ?? Assembly.GetEntryAssembly()?.GetName().Version
                          ?? new Version(0, 1, 0, 0);
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static string CalculateInformationalVersion()
    {
        Assembly? assembly = Assembly.GetExecutingAssembly();
        AssemblyInformationalVersionAttribute? attribute = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute?.InformationalVersion ?? GetApplicationVersion();
    }

    private static BuildInfo? LoadBuildInfo()
    {
        try
        {
            string buildInfoPath = Path.Combine(AppContext.BaseDirectory, "build-info.json");
            if (!File.Exists(buildInfoPath))
                return null;

            string json = File.ReadAllText(buildInfoPath);
            return JsonSerializer.Deserialize(json, VersionJsonContext.Default.BuildInfo);
        }
        catch
        {
            return null;
        }
    }

    private static string CalculateDisplayVersion()
    {
        BuildInfo? buildInfo = GetBuildInfo();
        return buildInfo?.FullVersion ?? GetInformationalVersion();
    }
}

public record BuildInfo(
    string Version,
    string BuildNumber,
    string FullVersion,
    string Timestamp,
    string GitCommit,
    string GitBranch
);