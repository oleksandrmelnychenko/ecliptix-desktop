using System;
using System.Collections.Generic;

namespace Ecliptix.Feature.Feed.Feed.Domain.Models;

public sealed record FeedPost
{
    public required string PostId { get; init; }
    public required PostAuthor Author { get; init; }
    public required PostContent Content { get; init; }
    public required PostInteraction Interaction { get; init; }
    public required DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? Location { get; init; }
    public List<Comment>? TopComments { get; init; }
    public bool IsEdited => UpdatedAt.HasValue;
}
