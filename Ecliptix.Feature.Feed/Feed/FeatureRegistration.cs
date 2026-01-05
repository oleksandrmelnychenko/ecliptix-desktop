using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Modularity.Modularity;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Feed.Feed;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<FeedModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddTransient<ViewModels.FeedViewModel>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.FeedViewModel, Views.FeedView>();
        factory.RegisterModuleViewModel(ModuleIdentifier.FEED, typeof(ViewModels.FeedViewModel));
    }
}
