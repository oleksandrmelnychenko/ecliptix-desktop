using Ecliptix.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Main;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<MainModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.MasterViewModel>();
    }
}
