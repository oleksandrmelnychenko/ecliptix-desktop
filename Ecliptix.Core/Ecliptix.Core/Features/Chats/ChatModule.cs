using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Modularity;
using Serilog;

namespace Ecliptix.Core.Features.Chats;

public record ChatModuleManifest() : ModuleManifest(
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.BACKGROUND,
    Dependencies: []
);

public class ChatModule : ModuleBase<ChatModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.CHATS;
    public override ChatModuleManifest Manifest { get; } = new();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });

        Log.Information("Chat module message handlers setup completed");
    }
}
