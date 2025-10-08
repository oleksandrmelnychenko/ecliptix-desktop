namespace Ecliptix.Utilities.Failures.Membership;

public enum LogoutFailureType
{
    NetworkRequestFailed,
    AlreadyLoggedOut,
    SessionNotFound,
    InvalidMembershipIdentifier,
    UnexpectedError
}
