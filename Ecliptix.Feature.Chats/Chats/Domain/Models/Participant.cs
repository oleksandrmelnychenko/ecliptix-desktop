using System;

namespace Ecliptix.Feature.Chats.Chats.Domain.Models;

public record Participant(Guid Id, string Name, string AvatarPath);
