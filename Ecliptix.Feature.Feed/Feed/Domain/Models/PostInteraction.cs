namespace Ecliptix.Feature.Feed.Feed.Domain.Models;

public sealed record PostInteraction
{
    public int LikesCount { get; init; }
    public int CommentsCount { get; init; }
    public int SavesCount { get; init; }
    public bool IsLikedByCurrentUser { get; init; }
    public bool IsSavedByCurrentUser { get; init; }
}
