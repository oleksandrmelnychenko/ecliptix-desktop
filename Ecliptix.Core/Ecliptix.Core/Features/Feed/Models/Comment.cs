using System;

namespace Ecliptix.Core.Features.Feed.Models;

public sealed record Comment
{
    public required string CommentId { get; init; }
    public required string PostId { get; init; }
    public required PostAuthor Author { get; init; }
    public required string Text { get; init; }
    public required DateTime CreatedAt { get; init; }
    public int LikesCount { get; init; }
    public bool IsLikedByCurrentUser { get; init; }
    public string? ParentCommentId { get; init; }
    public int RepliesCount { get; init; }
}
