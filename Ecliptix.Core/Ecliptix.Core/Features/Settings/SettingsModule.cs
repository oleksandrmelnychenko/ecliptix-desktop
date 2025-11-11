using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Modularity;
using Serilog;

namespace Ecliptix.Core.Features.Settings;

public record SettingsModuleManifest() : ModuleManifest(
    Priority: 40,
    LoadingStrategy: ModuleLoadingStrategy.LAZY,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class SettingsModule : ModuleBase<SettingsModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.SETTINGS;
    public override SettingsModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });

        Log.Information("Settings module message handlers setup completed");
    }
}
