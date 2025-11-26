using System;

namespace Ecliptix.Core.Features.Chats.Models;

public record MessageModel(
    Guid Id,
    Guid ChatId,
    Guid SenderId,
    string Text,
    DateTime Timestamp,
    MessageType Type,
    Guid? ReplyToMessageId = null
);
