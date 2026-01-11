using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Feed.Domain.Models;
using Ecliptix.Utilities;

namespace Ecliptix.Feature.Feed.Services.Abstractions;

public interface ICommentService
{
    Task<Result<CommentsPage, string>> LoadCommentsAsync(string postId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<Result<Comment, string>> PostCommentAsync(string postId, string text, string? parentCommentId = null, CancellationToken cancellationToken = default);
    Task<Result<Comment, string>> ToggleCommentLikeAsync(string commentId, CancellationToken cancellationToken = default);
    Task<Result<bool, string>> DeleteCommentAsync(string commentId, CancellationToken cancellationToken = default);
}

public sealed record CommentsPage
{
    public required List<Comment> Comments { get; init; }
    public required int CurrentPage { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
    public required bool HasNextPage { get; init; }
}
