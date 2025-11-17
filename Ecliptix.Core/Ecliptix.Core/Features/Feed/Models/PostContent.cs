namespace Ecliptix.Core.Features.Feed.Models;

public enum PostContentType
{
    Text,
    ImageCarousel,
    Video
}

public abstract record PostContent
{
    public abstract PostContentType ContentType { get; }
}
