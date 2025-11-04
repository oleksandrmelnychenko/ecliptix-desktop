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
                    UserError.ERROR_CODE,
                    UserError.I_18N_KEY,
                    UserError.Message,
                    UserError.RETRYABLE,
                    UserError.RETRY_AFTER_MILLISECONDS,
                    UserError.CORRELATION_ID,
                    UserError.LOCALE,
                    UserError.GrpcStatusCode
                }
        };
    }

    public static NetworkFailure InvalidRequestType(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.InvalidRequestType, details, inner) { UserError = userError };

    public static NetworkFailure DataCenterNotResponding(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.DataCenterNotResponding, details, inner) { UserError = userError };

    public static NetworkFailure DataCenterShutdown(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.DataCenterShutdown, details, inner) { UserError = userError };

    public static NetworkFailure RsaEncryption(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.RsaEncryptionFailure, details, inner) { UserError = userError };

    public static NetworkFailure ProtocolStateMismatch(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.ProtocolStateMismatch, details, inner) { UserError = userError };

    public static NetworkFailure OperationCancelled(string? details = null, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.OperationCancelled, details ?? "Operation was cancelled", inner)
        {
            UserError = userError
        };

    public static NetworkFailure CriticalAuthenticationFailure(string details, Exception? inner = null, UserFacingError? userError = null) =>
        new(NetworkFailureType.CriticalAuthenticationFailure, details, inner) { UserError = userError };

    public override GrpcErrorDescriptor ToGrpcDescriptor()
    {
        if (UserError is not null)
        {
            return new GrpcErrorDescriptor(
                UserError.ERROR_CODE,
                UserError.GrpcStatusCode ?? StatusCode.Internal,
                UserError.I_18N_KEY,
                UserError.RETRYABLE ?? false,
                UserError.RETRY_AFTER_MILLISECONDS);
        }

        return FailureType switch
        {
            NetworkFailureType.DataCenterNotResponding => new GrpcErrorDescriptor(
                ERROR_CODE.SERVICE_UNAVAILABLE,
                StatusCode.Unavailable,
                ErrorI18nKeys.SERVICE_UNAVAILABLE,
                RETRYABLE: true),
            NetworkFailureType.DataCenterShutdown => new GrpcErrorDescriptor(
                ERROR_CODE.SERVICE_UNAVAILABLE,
                StatusCode.Unavailable,
                ErrorI18nKeys.SERVICE_UNAVAILABLE),
            NetworkFailureType.InvalidRequestType => new GrpcErrorDescriptor(
                ERROR_CODE.ValidationFailed,
                StatusCode.InvalidArgument,
                ErrorI18nKeys.VALIDATION),
            NetworkFailureType.RsaEncryptionFailure => new GrpcErrorDescriptor(
                ERROR_CODE.InternalError,
                StatusCode.Internal,
                ErrorI18nKeys.INTERNAL),
            NetworkFailureType.ProtocolStateMismatch => new GrpcErrorDescriptor(
                ERROR_CODE.PRECONDITION_FAILED,
                StatusCode.FailedPrecondition,
                ErrorI18nKeys.PRECONDITION_FAILED),
            NetworkFailureType.OperationCancelled => new GrpcErrorDescriptor(
                ERROR_CODE.CANCELLED,
                StatusCode.Cancelled,
                ErrorI18nKeys.CANCELLED),
            NetworkFailureType.CriticalAuthenticationFailure => new GrpcErrorDescriptor(
                ERROR_CODE.UNAUTHENTICATED,
                StatusCode.Unauthenticated,
                ErrorI18nKeys.UNAUTHENTICATED),
            _ => new GrpcErrorDescriptor(
                ERROR_CODE.InternalError,
                StatusCode.Internal,
                ErrorI18nKeys.INTERNAL)
        };
    }
}
