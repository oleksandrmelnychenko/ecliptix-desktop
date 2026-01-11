using System.Threading.Tasks;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Chats;

public record ChatsModuleManifest() : ModuleManifest(
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.BACKGROUND,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class ChatsModule : ModuleBase<ChatsModuleManifest>, IModuleServiceRegistrar
{
    public override ModuleIdentifier Id => ModuleIdentifier.CHATS;
    public override ChatsModuleManifest Manifest { get; } = new();

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
