using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.Feed.Feed;

public record FeedModuleManifest() : ModuleManifest(
    Priority: 25,
    LoadingStrategy: ModuleLoadingStrategy.BACKGROUND,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class FeedModule : ModuleBase<FeedModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.FEED;
    public override FeedModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
