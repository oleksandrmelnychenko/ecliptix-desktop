using Ecliptix.Feature.Feed.Feed.Services.Abstractions;
using Ecliptix.Feature.Feed.Feed.Services.Implementation;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Ecliptix.Core.Desktop.DI;

public static class FeedServiceCollectionExtensions
{
    public static IServiceCollection AddFeedServices(this IServiceCollection services)
    {
        services.AddSingleton<IFeedService, FeedService>();
        services.AddSingleton<IPostInteractionService, PostInteractionService>();
        services.AddSingleton<ICommentService, CommentService>();

        Log.Information("Feed services configured successfully");
        return services;
    }
}
