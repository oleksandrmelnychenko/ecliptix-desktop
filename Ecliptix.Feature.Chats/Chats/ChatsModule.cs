using System.Threading.Tasks;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;

namespace Ecliptix.Feature.Chats.Chats;

public record ChatsModuleManifest() : ModuleManifest(
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.BACKGROUND,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class ChatsModule : ModuleBase<ChatsModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.CHATS;
    public override ChatsModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
