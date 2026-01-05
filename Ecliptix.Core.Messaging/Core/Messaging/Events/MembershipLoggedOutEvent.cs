namespace Ecliptix.Core.Messaging.Core.Messaging.Events;

public record MembershipLoggedOutEvent(string MembershipId, string Reason);
