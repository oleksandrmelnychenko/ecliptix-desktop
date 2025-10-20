using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Modularity;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Ecliptix.Core.Features.Main;

public record MainModuleManifest() : ModuleManifest(
    Priority: 20,
    LoadingStrategy: ModuleLoadingStrategy.Lazy,
    Dependencies: []
);

public class MainModule : ModuleBase<MainModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.Main;
    public override MainModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });

        Log.Information("Main module message handlers setup completed");
    }
}
