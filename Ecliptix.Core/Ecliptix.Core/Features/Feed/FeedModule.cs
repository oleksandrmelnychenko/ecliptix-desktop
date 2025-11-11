using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Modularity;
using Serilog;

namespace Ecliptix.Core.Features.Feed;

public record FeedModuleManifest() : ModuleManifest(
    Priority: 25,
    LoadingStrategy: ModuleLoadingStrategy.EAGER,
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

        Log.Information("Feed module message handlers setup completed");
    }
}
