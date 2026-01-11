using Ecliptix.Core.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Chats;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<ChatsModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.ChatsViewModel>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.ChatsViewModel, Views.ChatsView>();
        factory.RegisterModuleViewModel(ModuleIdentifier.CHATS, typeof(ViewModels.ChatsViewModel));
    }
}
