using System;

namespace Ecliptix.Feature.Chats.Domain.Models;

public record Participant(Guid Id, string Name, string AvatarPath);
