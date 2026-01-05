using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Profile.Profile;

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
