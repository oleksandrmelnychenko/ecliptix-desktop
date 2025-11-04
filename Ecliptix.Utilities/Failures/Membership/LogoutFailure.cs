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

    public static LogoutFailure SESSION_NOT_FOUND(string details, Exception? inner = null) =>
        new(LogoutFailureType.SESSION_NOT_FOUND, details, inner);

    public static LogoutFailure InvalidMembershipIdentifier(string details, Exception? inner = null) =>
        new(LogoutFailureType.InvalidMembershipIdentifier, details, inner);

    public static LogoutFailure CryptographicOperationFailed(string details, Exception? inner = null) =>
        new(LogoutFailureType.CryptographicOperationFailed, details, inner);

    public static LogoutFailure InvalidRevocationProof(string details, Exception? inner = null) =>
        new(LogoutFailureType.InvalidRevocationProof, details, inner);

    public static LogoutFailure UnexpectedError(string details, Exception? inner = null) =>
        new(LogoutFailureType.UnexpectedError, details, inner);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            LogoutFailureType.NetworkRequestFailed => new GrpcErrorDescriptor(
                ERROR_CODE.SERVICE_UNAVAILABLE, StatusCode.Unavailable, ErrorI18nKeys.SERVICE_UNAVAILABLE, RETRYABLE: true),
            LogoutFailureType.AlreadyLoggedOut => new GrpcErrorDescriptor(
                ERROR_CODE.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18nKeys.PRECONDITION_FAILED),
            LogoutFailureType.SESSION_NOT_FOUND => new GrpcErrorDescriptor(
                ERROR_CODE.NOT_FOUND, StatusCode.NotFound, ErrorI18nKeys.NOT_FOUND),
            LogoutFailureType.InvalidMembershipIdentifier => new GrpcErrorDescriptor(
                ERROR_CODE.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.VALIDATION),
            LogoutFailureType.CryptographicOperationFailed => new GrpcErrorDescriptor(
                ERROR_CODE.InternalError, StatusCode.Internal, ErrorI18nKeys.INTERNAL),
            LogoutFailureType.InvalidRevocationProof => new GrpcErrorDescriptor(
                ERROR_CODE.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.VALIDATION),
            _ => new GrpcErrorDescriptor(
                ERROR_CODE.InternalError, StatusCode.Internal, ErrorI18nKeys.INTERNAL)
        };
}
