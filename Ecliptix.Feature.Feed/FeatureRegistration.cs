using Ecliptix.Core.Modularity;
using Ecliptix.Feature.Feed.Services.Abstractions;
using Ecliptix.Feature.Feed.Services.Implementation;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Feed;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<FeedModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IFeedService, FeedService>();
        services.AddSingleton<IPostInteractionService, PostInteractionService>();
        services.AddSingleton<ICommentService, CommentService>();
        services.AddTransient<ViewModels.FeedViewModel>();
    }

    public static void RegisterViews(IModuleViewFactory factory)
    {
        factory.RegisterView<ViewModels.FeedViewModel, Views.FeedView>();
        factory.RegisterModuleViewModel(ModuleIdentifier.FEED, typeof(ViewModels.FeedViewModel));
    }
}
