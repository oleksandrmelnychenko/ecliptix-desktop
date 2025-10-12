namespace Ecliptix.Core.Core.Messaging.Events;

public record MembershipLoggedOutEvent(string MembershipId, string Reason);
