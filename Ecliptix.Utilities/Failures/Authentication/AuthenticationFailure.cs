using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Authentication;

public record AuthenticationFailure(
    AuthenticationFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog()
    {
        return new
        {
            AuthenticationFailureType = FailureType.ToString(),
            Message,
            InnerException = InnerException?.Message,
            Timestamp
        };
    }

    public static AuthenticationFailure InvalidCredentials(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.INVALID_CREDENTIALS, details, inner);

    public static AuthenticationFailure LoginAttemptExceeded(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.LOGIN_ATTEMPT_EXCEEDED, details, inner);

    public static AuthenticationFailure MobileNumberRequired(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.MOBILE_NUMBER_REQUIRED, details, inner);

    public static AuthenticationFailure SecureKeyRequired(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SECURE_KEY_REQUIRED, details, inner);

    public static AuthenticationFailure UnexpectedError(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.UNEXPECTED_ERROR, details, inner);

    public static AuthenticationFailure SECURE_MEMORY_ALLOCATION_FAILED(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SECURE_MEMORY_ALLOCATION_FAILED, details, inner);

    public static AuthenticationFailure SECURE_MEMORY_WRITE_FAILED(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SECURE_MEMORY_WRITE_FAILED, details, inner);

    public static AuthenticationFailure KeyDerivationFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.KEY_DERIVATION_FAILED, details, inner);

    public static AuthenticationFailure MasterKeyDerivationFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.MASTER_KEY_DERIVATION_FAILED, details, inner);

    public static AuthenticationFailure NetworkRequestFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.NETWORK_REQUEST_FAILED, details, inner);

    public static AuthenticationFailure InvalidMembershipIdentifier(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.INVALID_MEMBERSHIP_IDENTIFIER, details, inner);

    public static AuthenticationFailure IdentityStorageFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.IDENTITY_STORAGE_FAILED, details, inner);

    public static AuthenticationFailure CriticalAuthenticationError(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.CRITICAL_AUTHENTICATION_ERROR, details, inner);

    public static AuthenticationFailure KeychainCorrupted(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.KEYCHAIN_CORRUPTED, details, inner);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            AuthenticationFailureType.INVALID_CREDENTIALS => new GrpcErrorDescriptor(
                ErrorCode.UNAUTHENTICATED, StatusCode.Unauthenticated, ErrorI18NKeys.UNAUTHENTICATED),
            AuthenticationFailureType.LOGIN_ATTEMPT_EXCEEDED => new GrpcErrorDescriptor(
                ErrorCode.MAX_ATTEMPTS_REACHED, StatusCode.ResourceExhausted, ErrorI18NKeys.MAX_ATTEMPTS),
            AuthenticationFailureType.MOBILE_NUMBER_REQUIRED => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            AuthenticationFailureType.SECURE_KEY_REQUIRED => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            AuthenticationFailureType.NETWORK_REQUEST_FAILED => new GrpcErrorDescriptor(
                ErrorCode.SERVICE_UNAVAILABLE, StatusCode.Unavailable, ErrorI18NKeys.SERVICE_UNAVAILABLE, Retryable: true),
            AuthenticationFailureType.INVALID_MEMBERSHIP_IDENTIFIER => new GrpcErrorDescriptor(
                ErrorCode.NOT_FOUND, StatusCode.NotFound, ErrorI18NKeys.NOT_FOUND),
            AuthenticationFailureType.CRITICAL_AUTHENTICATION_ERROR => new GrpcErrorDescriptor(
                ErrorCode.UNAUTHENTICATED, StatusCode.Unauthenticated, ErrorI18NKeys.UNAUTHENTICATED),
            _ => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL)
        };
}
