using System;

namespace Ecliptix.Core.Features.Chats.Models;

public record Participant(Guid Id, string Name, string AvatarPath);
