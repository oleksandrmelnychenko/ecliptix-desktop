using System;
using System.Collections.Generic;

namespace Ecliptix.Core.Features.Chats.Models;

public record ChatModel(
    Guid Id,
    string Title,
    ChatType Type,
    List<Guid> ParticipantIds
);
