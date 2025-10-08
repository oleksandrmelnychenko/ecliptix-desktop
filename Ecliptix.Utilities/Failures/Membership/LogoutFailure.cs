namespace Ecliptix.Utilities.Failures.Membership;

public record LogoutFailure(
    LogoutFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog()
    {
        return new
        {
            LogoutFailureType = FailureType.ToString(),
            Message,
            InnerException = InnerException?.Message,
            Timestamp
        };
    }

    public static LogoutFailure NetworkRequestFailed(string details, Exception? inner = null) =>
        new(LogoutFailureType.NetworkRequestFailed, details, inner);

    public static LogoutFailure AlreadyLoggedOut(string details, Exception? inner = null) =>
        new(LogoutFailureType.AlreadyLoggedOut, details, inner);

    public static LogoutFailure SessionNotFound(string details, Exception? inner = null) =>
        new(LogoutFailureType.SessionNotFound, details, inner);

    public static LogoutFailure InvalidMembershipIdentifier(string details, Exception? inner = null) =>
        new(LogoutFailureType.InvalidMembershipIdentifier, details, inner);

    public static LogoutFailure UnexpectedError(string details, Exception? inner = null) =>
        new(LogoutFailureType.UnexpectedError, details, inner);
}
