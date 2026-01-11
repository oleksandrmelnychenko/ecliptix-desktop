namespace Ecliptix.Feature.Feed.Domain.Models;

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
