using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Network;

public record NetworkFailure(
    NetworkFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public UserFacingError? UserError { get; init; }
    public bool RequiresReinit { get; init; }

    public override object ToStructuredLog()
    {
        return new
        {
            NetworkFailureType = FailureType.ToString(),
            Message,
            InnerException,
            Timestamp,
            RequiresReinit,
            UserError = UserError is null
                ? null
                : new
                {
                    UserError.ErrorCode,
                    UserError.I18nKey,
                    UserError.Message,
                    UserError.Retryable,
                    UserError.RetryAfterMilliseconds,
                    UserError.CorrelationId,
                    UserError.Locale,
                    UserError.GrpcStatusCode
                }
        };
    }

    public static NetworkFailure InvalidRequestType(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.InvalidRequestType, details, inner) { UserError = userError };

    public static NetworkFailure DataCenterNotResponding(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.DataCenterNotResponding, details, inner) { UserError = userError };

    public static NetworkFailure DataCenterShutdown(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.DataCenterShutdown, details, inner) { UserError = userError };

    public static NetworkFailure RsaEncryption(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.RsaEncryptionFailure, details, inner) { UserError = userError };

    public static NetworkFailure ProtocolStateMismatch(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.ProtocolStateMismatch, details, inner) { UserError = userError };

    public static NetworkFailure OperationCancelled(string? details = null, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.OperationCancelled, details ?? "Operation was cancelled", inner)
        {
            UserError = userError
        };

    public static NetworkFailure CriticalAuthenticationFailure(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new NetworkFailure(NetworkFailureType.CriticalAuthenticationFailure, details, inner) { UserError = userError };

    public override GrpcErrorDescriptor ToGrpcDescriptor()
    {
        // If UserError is present, use it; otherwise map from FailureType
        if (UserError is not null)
        {
            return new GrpcErrorDescriptor(
                UserError.ErrorCode,
                UserError.GrpcStatusCode ?? StatusCode.Internal,
                UserError.I18nKey,
                UserError.Retryable ?? false,
                UserError.RetryAfterMilliseconds);
        }

        return FailureType switch
        {
            NetworkFailureType.DataCenterNotResponding => new GrpcErrorDescriptor(
                ErrorCode.ServiceUnavailable,
                StatusCode.Unavailable,
                ErrorI18nKeys.ServiceUnavailable,
                Retryable: true),
            NetworkFailureType.DataCenterShutdown => new GrpcErrorDescriptor(
                ErrorCode.ServiceUnavailable,
                StatusCode.Unavailable,
                ErrorI18nKeys.ServiceUnavailable),
            NetworkFailureType.InvalidRequestType => new GrpcErrorDescriptor(
                ErrorCode.ValidationFailed,
                StatusCode.InvalidArgument,
                ErrorI18nKeys.Validation),
            NetworkFailureType.RsaEncryptionFailure => new GrpcErrorDescriptor(
                ErrorCode.InternalError,
                StatusCode.Internal,
                ErrorI18nKeys.Internal),
            NetworkFailureType.ProtocolStateMismatch => new GrpcErrorDescriptor(
                ErrorCode.PreconditionFailed,
                StatusCode.FailedPrecondition,
                ErrorI18nKeys.PreconditionFailed),
            NetworkFailureType.OperationCancelled => new GrpcErrorDescriptor(
                ErrorCode.Cancelled,
                StatusCode.Cancelled,
                ErrorI18nKeys.Cancelled),
            NetworkFailureType.CriticalAuthenticationFailure => new GrpcErrorDescriptor(
                ErrorCode.Unauthenticated,
                StatusCode.Unauthenticated,
                ErrorI18nKeys.Unauthenticated),
            _ => new GrpcErrorDescriptor(
                ErrorCode.InternalError,
                StatusCode.Internal,
                ErrorI18nKeys.Internal)
        };
    }
}
