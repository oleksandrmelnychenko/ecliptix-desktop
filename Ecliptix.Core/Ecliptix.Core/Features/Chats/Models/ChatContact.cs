using System;

namespace Ecliptix.Core.Features.Chats.Models;

public sealed class ChatContact
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string PhoneNumber { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Avatar { get; init; } = string.Empty;
    public bool IsOnline { get; init; }
    public DateTime? LastSeen { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
    public string About { get; init; } = string.Empty;
}
