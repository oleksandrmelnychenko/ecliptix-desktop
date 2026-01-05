using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.Profile.Profile;

public record ProfileModuleManifest() : ModuleManifest(
    Priority: 40,
    LoadingStrategy: ModuleLoadingStrategy.LAZY,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class ProfileModule : ModuleBase<ProfileModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.PROFILE;
    public override ProfileModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
