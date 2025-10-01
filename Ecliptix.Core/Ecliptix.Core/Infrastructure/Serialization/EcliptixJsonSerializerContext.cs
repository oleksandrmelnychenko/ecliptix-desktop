using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
    {
        JsonTypeInfo<T>? typeInfo = EcliptixJsonSerializerContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
        if (typeInfo == null)
        {
            throw new NotSupportedException($"Type {typeof(T).Name} is not registered in EcliptixJsonSerializerContext. Add [JsonSerializable(typeof({typeof(T).Name}))] attribute.");
        }
        return JsonSerializer.Serialize(value, typeInfo);
    }

    public static T? Deserialize<T>(string json) where T : class
    {
        JsonTypeInfo<T>? typeInfo = EcliptixJsonSerializerContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
        if (typeInfo == null)
        {
            throw new NotSupportedException($"Type {typeof(T).Name} is not registered in EcliptixJsonSerializerContext. Add [JsonSerializable(typeof({typeof(T).Name}))] attribute.");
        }
        return JsonSerializer.Deserialize<T>(json, typeInfo);
    }

    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) where T : class
    {
        JsonTypeInfo<T>? typeInfo = EcliptixJsonSerializerContext.Default.GetTypeInfo(typeof(T)) as JsonTypeInfo<T>;
        if (typeInfo == null)
        {
            throw new NotSupportedException($"Type {typeof(T).Name} is not registered in EcliptixJsonSerializerContext. Add [JsonSerializable(typeof({typeof(T).Name}))] attribute.");
        }
        return JsonSerializer.Deserialize<T>(utf8Json, typeInfo);
    }
}