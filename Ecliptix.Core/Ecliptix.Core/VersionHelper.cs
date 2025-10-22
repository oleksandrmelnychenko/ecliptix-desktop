using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;
using Ecliptix.Utilities;

namespace Ecliptix.Core;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization)]
[JsonSerializable(typeof(BuildInfo))]
public partial class EcliptixJsonContext : JsonSerializerContext;

public static class VersionHelper
{
    private static readonly Lazy<string> ApplicationVersion = new(CalculateApplicationVersion);

    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "InformationalVersion is only used for display purposes and has fallback to ApplicationVersion")]
    private static readonly Lazy<string> InformationalVersion = new(CalculateInformationalVersion);

    private static readonly Lazy<Option<BuildInfo>> BuildInfo = new(LoadBuildInfo);
    private static readonly Lazy<string> DisplayVersion = new(CalculateDisplayVersion);

    public static string GetApplicationVersion() => ApplicationVersion.Value;

    public static string GetInformationalVersion() => InformationalVersion.Value;

    public static Option<BuildInfo> GetBuildInfo() => BuildInfo.Value;

    public static string GetDisplayVersion() => DisplayVersion.Value;

    private static string CalculateApplicationVersion()
    {
        return typeof(VersionHelper).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    [RequiresUnreferencedCode("Reads assembly attributes which may be trimmed")]
    private static string CalculateInformationalVersion()
    {
        try
        {
            string informationalVersion = typeof(VersionHelper).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                is System.Reflection.AssemblyInformationalVersionAttribute[] { Length: > 0 } attrs
                ? attrs[0].InformationalVersion
                : GetApplicationVersion();
            return informationalVersion;
        }
        catch
        {
            return GetApplicationVersion();
        }
    }

    private static Option<BuildInfo> LoadBuildInfo()
    {
        try
        {
            string buildInfoPath = Path.Combine(AppContext.BaseDirectory, "build-info.json");
            if (!File.Exists(buildInfoPath))
            {
                return Option<BuildInfo>.None;
            }

            string json = File.ReadAllText(buildInfoPath);
            return JsonSerializer.Deserialize(json, EcliptixJsonContext.Default.BuildInfo).ToOption();
        }
        catch
        {
            return Option<BuildInfo>.None;
        }
    }

    private static string CalculateDisplayVersion()
    {
        return GetBuildInfo()
            .Select(info => info.FullVersion)
            .ValueOr(GetInformationalVersion());
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
