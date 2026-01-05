using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.Authentication;

public record AuthenticationModuleManifest() : ModuleManifest(
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.EAGER,
    Dependencies: []
);

public class AuthenticationModule : ModuleBase<AuthenticationModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.AUTHENTICATION;
    public override AuthenticationModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
