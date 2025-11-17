namespace Ecliptix.Core.Features.Feed.Models;

public sealed record VideoContent : PostContent
{
    public override PostContentType ContentType => PostContentType.Video;
    public required string VideoUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int DurationSeconds { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string? Caption { get; init; }
    public bool HasAudio { get; init; }
}
