using System;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Features.Chats.Models;

public sealed class ChatConversation : ReactiveObject
{
    public string Id { get; init; } = string.Empty;
    public string ContactName { get; init; } = string.Empty;
    public string ContactAvatar { get; init; } = string.Empty;
    public string LastMessage { get; init; } = string.Empty;
    public DateTime LastMessageTime { get; init; }
    public int UnreadCount { get; init; }
    public bool IsOnline { get; init; }
    public bool IsPinned { get; init; }
    public ConversationType Type { get; init; }

    [Reactive] public bool IsTyping { get; set; }
    [Reactive] public bool IsSelected { get; set; }

    public string LastMessageTimeDisplay => GetTimeDisplay();

    private string GetTimeDisplay()
    {
        TimeSpan timeSince = DateTime.Now - LastMessageTime;

        if (timeSince.TotalMinutes < 1)
            return "Just now";
        if (timeSince.TotalMinutes < 60)
            return $"{(int)timeSince.TotalMinutes} min ago";
        if (timeSince.TotalHours < 24)
            return $"{(int)timeSince.TotalHours}h ago";
        if (timeSince.TotalDays < 7)
            return LastMessageTime.ToString("ddd");

        return LastMessageTime.ToString("MMM dd");
    }
}
