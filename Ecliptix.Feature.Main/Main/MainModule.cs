using System.Collections.Generic;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.Main.Main;

public record MainModuleManifest() : ModuleManifest(
    Priority: 20,
    LoadingStrategy: ModuleLoadingStrategy.LAZY,
    Dependencies: []
);

public class MainModule : ModuleBase<MainModuleManifest>
{
    private static readonly HashSet<ModuleIdentifier> AllowedContentModules =
    [
        ModuleIdentifier.FEED,
        ModuleIdentifier.CHATS,
        ModuleIdentifier.SETTINGS,
        ModuleIdentifier.PROFILE
    ];

    public override ModuleIdentifier Id => ModuleIdentifier.MAIN;
    public override MainModuleManifest Manifest { get; } = new();

    public static bool CanLoadContentModule(ModuleIdentifier moduleId) =>
        AllowedContentModules.Contains(moduleId);

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
