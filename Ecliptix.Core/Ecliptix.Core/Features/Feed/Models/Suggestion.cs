using System;

namespace Ecliptix.Core.Features.Feed.Models;

public sealed record Suggestion
{
    public required string Id { get; init; }
    public required PostAuthor Author { get; init; }
    public required string Text { get; set; }
    public string? Title { get; init; } 
    public int MutualConnectionsCount { get; init; }
    public bool IsFollowing { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
