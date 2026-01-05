using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.NewContent.NewContent;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<NewContentModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.CreateSelectionViewModel>();
        services.AddTransient<ViewModels.CreateWizardViewModel>();
        services.AddTransient<ViewModels.NewChannelViewModel>();
        services.AddTransient<ViewModels.NewContactViewModel>();
        services.AddTransient<ViewModels.NewGroupChatViewModel>();
        services.AddTransient<ViewModels.NewPostViewModel>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.CreateSelectionViewModel, Views.CreateSelectionView>();
        factory.RegisterView<ViewModels.CreateWizardViewModel, Views.CreateWizardView>();
        factory.RegisterView<ViewModels.NewChannelViewModel, Views.NewChannelView>();
        factory.RegisterView<ViewModels.NewContactViewModel, Views.NewContactView>();
        factory.RegisterView<ViewModels.NewGroupChatViewModel, Views.NewGroupChatView>();
        factory.RegisterView<ViewModels.NewPostViewModel, Views.NewPostView>();
    }
}
