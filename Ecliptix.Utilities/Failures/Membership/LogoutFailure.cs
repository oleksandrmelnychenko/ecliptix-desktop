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
        new(LogoutFailureType.NETWORK_REQUEST_FAILED, details, inner);

    public static LogoutFailure AlreadyLoggedOut(string details, Exception? inner = null) =>
        new(LogoutFailureType.ALREADY_LOGGED_OUT, details, inner);

    public static LogoutFailure SESSION_NOT_FOUND(string details, Exception? inner = null) =>
        new(LogoutFailureType.SESSION_NOT_FOUND, details, inner);

    public static LogoutFailure InvalidMembershipIdentifier(string details, Exception? inner = null) =>
        new(LogoutFailureType.INVALID_MEMBERSHIP_IDENTIFIER, details, inner);

    public static LogoutFailure CryptographicOperationFailed(string details, Exception? inner = null) =>
        new(LogoutFailureType.CRYPTOGRAPHIC_OPERATION_FAILED, details, inner);

    public static LogoutFailure InvalidRevocationProof(string details, Exception? inner = null) =>
        new(LogoutFailureType.INVALID_REVOCATION_PROOF, details, inner);

    public static LogoutFailure UnexpectedError(string details, Exception? inner = null) =>
        new(LogoutFailureType.UNEXPECTED_ERROR, details, inner);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            LogoutFailureType.NETWORK_REQUEST_FAILED => new GrpcErrorDescriptor(
                ErrorCode.SERVICE_UNAVAILABLE, StatusCode.Unavailable, ErrorI18NKeys.SERVICE_UNAVAILABLE, Retryable: true),
            LogoutFailureType.ALREADY_LOGGED_OUT => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18NKeys.PRECONDITION_FAILED),
            LogoutFailureType.SESSION_NOT_FOUND => new GrpcErrorDescriptor(
                ErrorCode.NOT_FOUND, StatusCode.NotFound, ErrorI18NKeys.NOT_FOUND),
            LogoutFailureType.INVALID_MEMBERSHIP_IDENTIFIER => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            LogoutFailureType.CRYPTOGRAPHIC_OPERATION_FAILED => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL),
            LogoutFailureType.INVALID_REVOCATION_PROOF => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            _ => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL)
        };
}
