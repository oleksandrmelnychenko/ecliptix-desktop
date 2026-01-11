using System.Threading.Tasks;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Feed;

public record FeedModuleManifest() : ModuleManifest(
    Priority: 25,
    LoadingStrategy: ModuleLoadingStrategy.BACKGROUND,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class FeedModule : ModuleBase<FeedModuleManifest>, IModuleServiceRegistrar
{
    public override ModuleIdentifier Id => ModuleIdentifier.FEED;
    public override FeedModuleManifest Manifest { get; } = new();

    public void RegisterServices(IServiceCollection services)
    {
        FeatureRegistration.RegisterServices(services);
    }

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
