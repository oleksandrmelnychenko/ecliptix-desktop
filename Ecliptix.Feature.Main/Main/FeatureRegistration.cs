using Ecliptix.Core.Modularity.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Main.Main;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<MainModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.MasterViewModel>();
    }
}
