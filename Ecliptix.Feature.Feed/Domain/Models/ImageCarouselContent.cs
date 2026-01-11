using System.Collections.Generic;

namespace Ecliptix.Feature.Feed.Domain.Models;

public sealed record ImageCarouselContent : PostContent
{
    public override PostContentType ContentType => PostContentType.ImageCarousel;
    public required List<ImageItem> Images { get; init; }
    public string? Caption { get; init; }
}

public sealed record ImageItem
{
    public required string ImageUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? AltText { get; init; }
}
