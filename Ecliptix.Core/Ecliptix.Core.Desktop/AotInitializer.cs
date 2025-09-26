using System.Diagnostics.CodeAnalysis;
using Ecliptix.Core.Infrastructure.AOT;

namespace Ecliptix.Core.Desktop;

public static class AotInitializer
{
    [RequiresUnreferencedCode("This method ensures types are preserved for AOT but uses reflection")]
    [RequiresDynamicCode("This method preserves types that may be created dynamically")]
    public static void Initialize()
    {
        AotAttributeHelper.PreserveViewModels();
        AotAttributeHelper.PreserveViews();
        AotAttributeHelper.PreserveReactiveCommands();

        PreserveJsonTypes();
        PreserveGrpcTypes();
        PreserveLoggingTypes();
        PreserveConfigurationTypes();
    }

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed")]
    private static void PreserveJsonTypes()
    {
        _ = typeof(System.Text.Json.JsonSerializer);
        _ = typeof(System.Text.Json.JsonSerializerOptions);
        _ = typeof(System.Text.Json.Serialization.JsonSerializerContext);
        _ = typeof(Infrastructure.Serialization.EcliptixJsonSerializerContext);
    }

    [RequiresUnreferencedCode("gRPC may require types that cannot be statically analyzed")]
    private static void PreserveGrpcTypes()
    {
        _ = typeof(Grpc.Net.Client.GrpcChannel);
        _ = typeof(Grpc.Core.ClientBase);
        _ = typeof(Google.Protobuf.IMessage);
    }

    [RequiresUnreferencedCode("Logging may require types that cannot be statically analyzed")]
    private static void PreserveLoggingTypes()
    {
        _ = typeof(Serilog.ILogger);
        _ = typeof(Serilog.Log);
        _ = typeof(Microsoft.Extensions.Logging.ILogger);
        _ = typeof(Microsoft.Extensions.Logging.ILoggerFactory);
    }

    [RequiresUnreferencedCode("Configuration may require types that cannot be statically analyzed")]
    private static void PreserveConfigurationTypes()
    {
        _ = typeof(Microsoft.Extensions.Configuration.IConfiguration);
        _ = typeof(Microsoft.Extensions.Configuration.ConfigurationBuilder);
        _ = typeof(Microsoft.Extensions.Options.IOptions<>);
    }
}