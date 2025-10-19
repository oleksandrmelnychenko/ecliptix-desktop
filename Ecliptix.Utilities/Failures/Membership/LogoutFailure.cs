using Grpc.Core;

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

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            LogoutFailureType.NetworkRequestFailed => new GrpcErrorDescriptor(
                ErrorCode.ServiceUnavailable, StatusCode.Unavailable, ErrorI18nKeys.ServiceUnavailable, Retryable: true),
            LogoutFailureType.AlreadyLoggedOut => new GrpcErrorDescriptor(
                ErrorCode.PreconditionFailed, StatusCode.FailedPrecondition, ErrorI18nKeys.PreconditionFailed),
            LogoutFailureType.SessionNotFound => new GrpcErrorDescriptor(
                ErrorCode.NotFound, StatusCode.NotFound, ErrorI18nKeys.NotFound),
            LogoutFailureType.InvalidMembershipIdentifier => new GrpcErrorDescriptor(
                ErrorCode.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.Validation),
            _ => new GrpcErrorDescriptor(
                ErrorCode.InternalError, StatusCode.Internal, ErrorI18nKeys.Internal)
        };
}
