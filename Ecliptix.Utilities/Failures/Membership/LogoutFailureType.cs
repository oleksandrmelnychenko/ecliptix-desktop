namespace Ecliptix.Utilities.Failures.Membership;

public enum LogoutFailureType
{
    NetworkRequestFailed,
    AlreadyLoggedOut,
    SESSION_NOT_FOUND,
    InvalidMembershipIdentifier,
    CryptographicOperationFailed,
    InvalidRevocationProof,
    UnexpectedError
}
