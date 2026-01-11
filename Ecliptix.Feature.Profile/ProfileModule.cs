using System.Threading.Tasks;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Profile;

public record ProfileModuleManifest() : ModuleManifest(
    Priority: 40,
    LoadingStrategy: ModuleLoadingStrategy.LAZY,
    Dependencies: [ModuleIdentifier.MAIN]
);

public class ProfileModule : ModuleBase<ProfileModuleManifest>, IModuleServiceRegistrar
{
    public override ModuleIdentifier Id => ModuleIdentifier.PROFILE;
    public override ProfileModuleManifest Manifest { get; } = new();

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
