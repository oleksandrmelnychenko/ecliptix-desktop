namespace Ecliptix.Core.Core.Messaging.Events;

public record UserLoggedOutEvent(string MembershipId, string Reason);
