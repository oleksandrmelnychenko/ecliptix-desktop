using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ecliptix.Core;

// AOT-friendly JSON source generation context
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BuildInfo))]
public partial class VersionJsonContext : JsonSerializerContext
{
}

public static class VersionHelper
{
    private static readonly Lazy<string> _applicationVersion = new(CalculateApplicationVersion);
    private static readonly Lazy<string> _fullVersion = new(CalculateFullVersion);
    private static readonly Lazy<string> _informationalVersion = new(CalculateInformationalVersion);
    private static readonly Lazy<BuildInfo?> _buildInfo = new(LoadBuildInfo);
    private static readonly Lazy<string> _displayVersion = new(CalculateDisplayVersion);
    
    public static string GetApplicationVersion() => _applicationVersion.Value;
    
    public static string GetFullVersion() => _fullVersion.Value;
    
    public static string GetInformationalVersion() => _informationalVersion.Value;
    
    public static BuildInfo? GetBuildInfo() => _buildInfo.Value;
    
    public static string GetDisplayVersion() => _displayVersion.Value;
    
    private static string CalculateApplicationVersion()
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version
                          ?? Assembly.GetEntryAssembly()?.GetName().Version
                          ?? new Version(0, 1, 0, 0);
        return $"{version.Major}.{version.Minor}.{version.Build}"; 
    }
    
    private static string CalculateFullVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version
                      ?? Assembly.GetEntryAssembly()?.GetName().Version
                      ?? new Version(0, 1, 0, 0);
        return version.ToString();
    }
    
    private static string CalculateInformationalVersion()
    {
        var assembly = Assembly.GetExecutingAssembly() ?? Assembly.GetEntryAssembly();
        var attribute = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute?.InformationalVersion ?? GetApplicationVersion();
    }
    
    private static BuildInfo? LoadBuildInfo()
    {
        try
        {
            var buildInfoPath = Path.Combine(AppContext.BaseDirectory, "build-info.json");
            if (!File.Exists(buildInfoPath))
                return null;
                
            var json = File.ReadAllText(buildInfoPath);
            // Use AOT-friendly JSON source generation
            return JsonSerializer.Deserialize(json, VersionJsonContext.Default.BuildInfo);
        }
        catch
        {
            return null;
        }
    }
    
    private static string CalculateDisplayVersion()
    {
        var buildInfo = GetBuildInfo();
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