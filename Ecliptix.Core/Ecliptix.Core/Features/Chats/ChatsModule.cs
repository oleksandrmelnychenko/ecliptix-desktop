using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Modularity;
using Serilog;

namespace Ecliptix.Core.Features.Chats;

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

        Log.Information("Chats module message handlers setup completed");
    }
}
