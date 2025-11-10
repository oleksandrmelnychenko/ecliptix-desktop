using System;

namespace Ecliptix.Core.Features.Chats.Models;

public sealed class ChatMessage
{
    public string Id { get; init; } = string.Empty;
    public string ConversationId { get; init; } = string.Empty;
    public string SenderId { get; init; } = string.Empty;
    public string SenderName { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
    public bool IsSent { get; init; }
    public bool IsRead { get; init; }
    public MessageStatus Status { get; init; }
    public MessageType Type { get; init; }
}
