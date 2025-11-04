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
        new(AuthenticationFailureType.InvalidCredentials, details, inner);

    public static AuthenticationFailure LoginAttemptExceeded(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.LoginAttemptExceeded, details, inner);

    public static AuthenticationFailure MobileNumberRequired(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.MobileNumberRequired, details, inner);

    public static AuthenticationFailure SecureKeyRequired(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SecureKeyRequired, details, inner);

    public static AuthenticationFailure UnexpectedError(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.UnexpectedError, details, inner);

    public static AuthenticationFailure SECURE_MEMORY_ALLOCATION_FAILED(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SECURE_MEMORY_ALLOCATION_FAILED, details, inner);

    public static AuthenticationFailure SECURE_MEMORY_WRITE_FAILED(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SECURE_MEMORY_WRITE_FAILED, details, inner);

    public static AuthenticationFailure KeyDerivationFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.KeyDerivationFailed, details, inner);

    public static AuthenticationFailure MasterKeyDerivationFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.MasterKeyDerivationFailed, details, inner);

    public static AuthenticationFailure NetworkRequestFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.NetworkRequestFailed, details, inner);

    public static AuthenticationFailure InvalidMembershipIdentifier(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.InvalidMembershipIdentifier, details, inner);

    public static AuthenticationFailure HmacKeyGenerationFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.HmacKeyGenerationFailed, details, inner);

    public static AuthenticationFailure KeySplittingFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.KeySplittingFailed, details, inner);

    public static AuthenticationFailure KeyStorageFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.KeyStorageFailed, details, inner);

    public static AuthenticationFailure IdentityStorageFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.IdentityStorageFailed, details, inner);

    public static AuthenticationFailure IdentityNotFound(string membershipId, Exception? inner = null) =>
        new(AuthenticationFailureType.IdentityNotFound, $"Identity not found for membership: {membershipId}", inner);

    public static AuthenticationFailure IdentityLoadFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.IdentityLoadFailed, details, inner);

    public static AuthenticationFailure CriticalAuthenticationError(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.CriticalAuthenticationError, details, inner);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            AuthenticationFailureType.InvalidCredentials => new GrpcErrorDescriptor(
                ERROR_CODE.UNAUTHENTICATED, StatusCode.Unauthenticated, ErrorI18nKeys.UNAUTHENTICATED),
            AuthenticationFailureType.LoginAttemptExceeded => new GrpcErrorDescriptor(
                ERROR_CODE.MaxAttemptsReached, StatusCode.ResourceExhausted, ErrorI18nKeys.MAX_ATTEMPTS),
            AuthenticationFailureType.MobileNumberRequired => new GrpcErrorDescriptor(
                ERROR_CODE.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.VALIDATION),
            AuthenticationFailureType.SecureKeyRequired => new GrpcErrorDescriptor(
                ERROR_CODE.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.VALIDATION),
            AuthenticationFailureType.NetworkRequestFailed => new GrpcErrorDescriptor(
                ERROR_CODE.SERVICE_UNAVAILABLE, StatusCode.Unavailable, ErrorI18nKeys.SERVICE_UNAVAILABLE, RETRYABLE: true),
            AuthenticationFailureType.InvalidMembershipIdentifier => new GrpcErrorDescriptor(
                ERROR_CODE.NOT_FOUND, StatusCode.NotFound, ErrorI18nKeys.NOT_FOUND),
            AuthenticationFailureType.CriticalAuthenticationError => new GrpcErrorDescriptor(
                ERROR_CODE.UNAUTHENTICATED, StatusCode.Unauthenticated, ErrorI18nKeys.UNAUTHENTICATED),
            _ => new GrpcErrorDescriptor(
                ERROR_CODE.InternalError, StatusCode.Internal, ErrorI18nKeys.INTERNAL)
        };
}
