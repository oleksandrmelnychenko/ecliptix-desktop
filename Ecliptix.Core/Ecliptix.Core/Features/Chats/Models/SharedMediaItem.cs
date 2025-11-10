using System;

namespace Ecliptix.Core.Features.Chats.Models;

public sealed class SharedMediaItem
{
    public string Id { get; init; } = string.Empty;
    public MessageType Type { get; init; }
    public string ThumbnailPath { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long Size { get; init; }
    public DateTime Timestamp { get; init; }
}
