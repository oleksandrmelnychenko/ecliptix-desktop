using Ecliptix.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Profile;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<ProfileModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.ProfileViewModel>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.ProfileViewModel, Views.ProfileView>();
        factory.RegisterModuleViewModel(ModuleIdentifier.PROFILE, typeof(ViewModels.ProfileViewModel));
    }
}
