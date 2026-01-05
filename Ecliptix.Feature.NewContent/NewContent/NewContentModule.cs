using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.NewContent.NewContent;

public record NewContentModuleManifest() : ModuleManifest(
    Priority: 40,
    LoadingStrategy: ModuleLoadingStrategy.BACKGROUND,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class NewContentModule : ModuleBase<NewContentModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.NEW_CONTENT;
    public override NewContentModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
