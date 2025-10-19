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

    public static AuthenticationFailure PasswordRequired(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.PasswordRequired, details, inner);

    public static AuthenticationFailure UnexpectedError(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.UnexpectedError, details, inner);

    public static AuthenticationFailure SecureMemoryAllocationFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SecureMemoryAllocationFailed, details, inner);

    public static AuthenticationFailure SecureMemoryWriteFailed(string details, Exception? inner = null) =>
        new(AuthenticationFailureType.SecureMemoryWriteFailed, details, inner);

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
                ErrorCode.Unauthenticated, StatusCode.Unauthenticated, ErrorI18nKeys.Unauthenticated),
            AuthenticationFailureType.LoginAttemptExceeded => new GrpcErrorDescriptor(
                ErrorCode.MaxAttemptsReached, StatusCode.ResourceExhausted, ErrorI18nKeys.MaxAttempts),
            AuthenticationFailureType.MobileNumberRequired => new GrpcErrorDescriptor(
                ErrorCode.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.Validation),
            AuthenticationFailureType.PasswordRequired => new GrpcErrorDescriptor(
                ErrorCode.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.Validation),
            AuthenticationFailureType.NetworkRequestFailed => new GrpcErrorDescriptor(
                ErrorCode.ServiceUnavailable, StatusCode.Unavailable, ErrorI18nKeys.ServiceUnavailable, Retryable: true),
            AuthenticationFailureType.InvalidMembershipIdentifier => new GrpcErrorDescriptor(
                ErrorCode.NotFound, StatusCode.NotFound, ErrorI18nKeys.NotFound),
            AuthenticationFailureType.CriticalAuthenticationError => new GrpcErrorDescriptor(
                ErrorCode.Unauthenticated, StatusCode.Unauthenticated, ErrorI18nKeys.Unauthenticated),
            _ => new GrpcErrorDescriptor(
                ErrorCode.InternalError, StatusCode.Internal, ErrorI18nKeys.Internal)
        };
}
