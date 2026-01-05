using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.Settings.Settings;

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
    }
}
