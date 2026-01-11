using System.Collections.Generic;
using System.Threading.Tasks;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Main;
using Ecliptix.Core.Modularity.Messaging;
using Ecliptix.Feature.Main.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Main;

public record MainModuleManifest() : ModuleManifest(
    Priority: 20,
    LoadingStrategy: ModuleLoadingStrategy.LAZY,
    Dependencies: []
);

public class MainModule : ModuleBase<MainModuleManifest>, IModuleServiceRegistrar
{
    private static readonly HashSet<ModuleIdentifier> AllowedContentModules =
    [
        ModuleIdentifier.FEED,
        ModuleIdentifier.CHATS,
        ModuleIdentifier.SETTINGS,
        ModuleIdentifier.PROFILE
    ];

    public override ModuleIdentifier Id => ModuleIdentifier.MAIN;
    public override MainModuleManifest Manifest { get; } = new();

    public void RegisterServices(IServiceCollection services)
    {
        FeatureRegistration.RegisterServices(services);
        services.AddTransient<IMainHost>(sp => sp.GetRequiredService<MasterViewModel>());
    }

    public static bool CanLoadContentModule(ModuleIdentifier moduleId) =>
        AllowedContentModules.Contains(moduleId);

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
