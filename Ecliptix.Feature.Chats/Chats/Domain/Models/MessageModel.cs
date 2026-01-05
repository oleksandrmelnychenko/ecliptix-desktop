using System;

namespace Ecliptix.Feature.Chats.Chats.Domain.Models;

public record MessageModel(
    Guid Id,
    Guid ChatId,
    Guid SenderId,
    string Text,
    DateTime Timestamp,
    MessageType Type,
    Guid? ReplyToMessageId = null
);
