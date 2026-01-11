using System.Collections.Generic;

namespace Ecliptix.Feature.Feed.Domain.Models;

public sealed record TextContent : PostContent
{
    public override PostContentType ContentType => PostContentType.Text;
    public required string Text { get; init; }
    public List<string>? Hashtags { get; init; }
    public List<string>? Mentions { get; init; }
}
