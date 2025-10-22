using System.Text.Json.Serialization;
using Ecliptix.AutoUpdater.Models;

namespace Ecliptix.AutoUpdater;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(UpdateManifest))]
[JsonSerializable(typeof(PlatformUpdate))]
internal partial class UpdateJsonContext : JsonSerializerContext
{
}
