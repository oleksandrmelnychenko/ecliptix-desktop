using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Infrastructure.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(BuildInfo))]
[JsonSerializable(typeof(DefaultSystemSettings))]
public partial class EcliptixJsonSerializerContext : JsonSerializerContext
{
    public static JsonSerializerOptions DefaultOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = Default
    };
}

public static class JsonSerializerHelper
{
    public static string Serialize<T>(T value) where T : class
        => JsonSerializer.Serialize(value, EcliptixJsonSerializerContext.DefaultOptions);

    public static T? Deserialize<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json, EcliptixJsonSerializerContext.DefaultOptions);

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) where T : class
        => JsonSerializer.Deserialize<T>(utf8Json, EcliptixJsonSerializerContext.DefaultOptions);
}