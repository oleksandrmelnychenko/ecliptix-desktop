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

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.MasterViewModel, Views.MasterView>();
        factory.RegisterModuleViewModel(ModuleIdentifier.MAIN, typeof(ViewModels.MasterViewModel));
    }
}
