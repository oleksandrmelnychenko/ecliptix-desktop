using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Communication;
using Ecliptix.Core.Core.Modularity;
using Serilog;

namespace Ecliptix.Core.Features.Main;

public record MainModuleManifest() : ModuleManifest(
    Priority: 20,
    LoadingStrategy: ModuleLoadingStrategy.LAZY,
    Dependencies: []
);

public class MainModule : ModuleBase<MainModuleManifest>
{
    private static readonly HashSet<ModuleIdentifier> _allowedContentModules = new()
    {
        ModuleIdentifier.FEED,
        ModuleIdentifier.CHATS,
        ModuleIdentifier.SETTINGS
    };

    public override ModuleIdentifier Id => ModuleIdentifier.MAIN;
    public override MainModuleManifest Manifest { get; } = new();

    public static bool CanLoadContentModule(ModuleIdentifier moduleId) =>
        _allowedContentModules.Contains(moduleId);

    public static IReadOnlyCollection<ModuleIdentifier> GetAllowedContentModules() =>
        _allowedContentModules.ToList();

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
