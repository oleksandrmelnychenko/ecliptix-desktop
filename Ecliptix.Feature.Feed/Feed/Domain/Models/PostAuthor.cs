namespace Ecliptix.Feature.Feed.Feed.Domain.Models;

public sealed record PostAuthor
{
    public required string UserId { get; init; }
    public required string Username { get; init; }
    public required string DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public bool IsVerified { get; init; }
}
