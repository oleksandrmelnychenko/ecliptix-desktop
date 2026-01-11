using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Feed.Domain.Models;
using Ecliptix.Utilities;

namespace Ecliptix.Feature.Feed.Services.Abstractions;

public interface IFeedService
{
    Task<Result<FeedPage, string>> LoadFeedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<FeedPage, string>> RefreshFeedAsync(int pageSize, CancellationToken cancellationToken = default);
    Task<Result<FeedPost, string>> GetPostByIdAsync(string postId, CancellationToken cancellationToken = default);
}

public sealed record FeedPage
{
    public required List<FeedPost> Posts { get; init; }
    public required int CurrentPage { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public required bool HasNextPage { get; init; }
}
