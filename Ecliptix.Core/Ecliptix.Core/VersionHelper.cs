using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace Ecliptix.Core;

public static class VersionHelper
{
    public static string GetApplicationVersion()
    {
        Version version = Assembly.GetExecutingAssembly().GetName().Version
                          ?? Assembly.GetEntryAssembly()?.GetName().Version
                          ?? new Version(0, 1, 0, 0);
        return $"{version.Major}.{version.Minor}.{version.Build}"; 
    }
    
    public static string GetFullVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version
                      ?? Assembly.GetEntryAssembly()?.GetName().Version
                      ?? new Version(0, 1, 0, 0);
        return version.ToString();
    }
    
    public static string GetInformationalVersion()
    {
        var assembly = Assembly.GetExecutingAssembly() ?? Assembly.GetEntryAssembly();
        var attribute = assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attribute?.InformationalVersion ?? GetApplicationVersion();
    }
    
    public static BuildInfo? GetBuildInfo()
    {
        try
        {
            var buildInfoPath = Path.Combine(AppContext.BaseDirectory, "build-info.json");
            if (!File.Exists(buildInfoPath))
                return null;
                
            var json = File.ReadAllText(buildInfoPath);
            return JsonSerializer.Deserialize<BuildInfo>(json, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
        }
        catch
        {
            return null;
        }
    }
    
    public static string GetDisplayVersion()
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