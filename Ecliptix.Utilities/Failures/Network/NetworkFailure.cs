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
                    ERROR_CODE = UserError.ErrorCode,
                    I_18N_KEY = UserError.I18NKey,
                    UserError.Message,
                    RETRYABLE = UserError.Retryable,
                    RETRY_AFTER_MILLISECONDS = UserError.RetryAfterMilliseconds,
                    CORRELATION_ID = UserError.CorrelationId,
                    LOCALE = UserError.Locale,
                    UserError.GrpcStatusCode
                }
        };
    }

    public static NetworkFailure InvalidRequestType(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.INVALID_REQUEST_TYPE, details, inner) { UserError = userError };

    public static NetworkFailure DataCenterNotResponding(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.DATA_CENTER_NOT_RESPONDING, details, inner) { UserError = userError };

    public static NetworkFailure DataCenterShutdown(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.DATA_CENTER_SHUTDOWN, details, inner) { UserError = userError };

    public static NetworkFailure RsaEncryption(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.RSA_ENCRYPTION_FAILURE, details, inner) { UserError = userError };

    public static NetworkFailure ProtocolStateMismatch(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.PROTOCOL_STATE_MISMATCH, details, inner) { UserError = userError };

    public static NetworkFailure OperationCancelled(string? details = null, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.OPERATION_CANCELLED, details ?? "Operation was cancelled", inner)
        {
            UserError = userError
        };

    public static NetworkFailure CriticalAuthenticationFailure(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE, details, inner) { UserError = userError };

    public override GrpcErrorDescriptor ToGrpcDescriptor()
    {
        if (UserError is not null)
        {
            return new GrpcErrorDescriptor(
                UserError.ErrorCode,
                UserError.GrpcStatusCode ?? StatusCode.Internal,
                UserError.I18NKey,
                UserError.Retryable ?? false,
                UserError.RetryAfterMilliseconds);
        }

        return FailureType switch
        {
            NetworkFailureType.DATA_CENTER_NOT_RESPONDING => new GrpcErrorDescriptor(
                ErrorCode.SERVICE_UNAVAILABLE,
                StatusCode.Unavailable,
                ErrorI18NKeys.SERVICE_UNAVAILABLE,
                Retryable: true),
            NetworkFailureType.DATA_CENTER_SHUTDOWN => new GrpcErrorDescriptor(
                ErrorCode.SERVICE_UNAVAILABLE,
                StatusCode.Unavailable,
                ErrorI18NKeys.SERVICE_UNAVAILABLE),
            NetworkFailureType.INVALID_REQUEST_TYPE => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED,
                StatusCode.InvalidArgument,
                ErrorI18NKeys.VALIDATION),
            NetworkFailureType.RSA_ENCRYPTION_FAILURE => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR,
                StatusCode.Internal,
                ErrorI18NKeys.INTERNAL),
            NetworkFailureType.PROTOCOL_STATE_MISMATCH => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED,
                StatusCode.FailedPrecondition,
                ErrorI18NKeys.PRECONDITION_FAILED),
            NetworkFailureType.OPERATION_CANCELLED => new GrpcErrorDescriptor(
                ErrorCode.CANCELLED,
                StatusCode.Cancelled,
                ErrorI18NKeys.CANCELLED),
            NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE => new GrpcErrorDescriptor(
                ErrorCode.UNAUTHENTICATED,
                StatusCode.Unauthenticated,
                ErrorI18NKeys.UNAUTHENTICATED),
            _ => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR,
                StatusCode.Internal,
                ErrorI18NKeys.INTERNAL)
        };
    }
}
