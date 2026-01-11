using System;
using System.Collections.Generic;

namespace Ecliptix.Feature.Chats.Domain.Models;

public record ChatModel(
    Guid Id,
    string Title,
    ChatType Type,
    List<Guid> ParticipantIds
);
