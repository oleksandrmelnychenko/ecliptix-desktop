using Ecliptix.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Settings;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<SettingsModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.SettingsViewModel>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.SettingsViewModel, Views.SettingsView>();
        factory.RegisterModuleViewModel(ModuleIdentifier.SETTINGS, typeof(ViewModels.SettingsViewModel));
    }
}
