using System.Globalization;
using Ecliptix.Network.Infrastructure.Network.Core.Constants;
using Ecliptix.Network.Services.Network.Resilience;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Network.Services.Network.Rpc;

public sealed class GrpcErrorProcessor : IGrpcErrorProcessor
{
    public NetworkFailure Process(RpcException rpcException) => CreateFailure(rpcException);

    public Task<NetworkFailure> ProcessAsync(RpcException rpcException) =>
        Task.FromResult(CreateFailure(rpcException));

    private NetworkFailure CreateFailure(RpcException rpcException)
    {
        UserFacingError userError = CreateUserFacingError(rpcException);
        NetworkFailureType failureType = DetermineFailureType(rpcException, userError);

        return failureType switch
        {
            NetworkFailureType.INVALID_REQUEST_TYPE => NetworkFailure.InvalidRequestType(userError.Message,
                rpcException,
                userError),
            NetworkFailureType.DATA_CENTER_SHUTDOWN => NetworkFailure.DataCenterShutdown(userError.Message,
                rpcException,
                userError),
            NetworkFailureType.PROTOCOL_STATE_MISMATCH => NetworkFailure.ProtocolStateMismatch(userError.Message,
                rpcException, userError),
            NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE => NetworkFailure.CriticalAuthenticationFailure(
                userError.Message, rpcException, userError),
            NetworkFailureType.OPERATION_CANCELLED => NetworkFailure.OperationCancelled(userError.Message, rpcException,
                userError),
            _ => NetworkFailure.DataCenterNotResponding(userError.Message, rpcException, userError)
        };
    }

    private UserFacingError CreateUserFacingError(RpcException rpcException)
    {
        Metadata trailers = rpcException.Trailers;

        ErrorCode errorCode = ParseErrorCode(rpcException, trailers);
        string? requestedKey = GetMetadataValue(trailers, GrpcErrorMetadataKeys.I_18_N_KEY);
        string message = ResolveMessage(errorCode, rpcException.Status.Detail);
        string keyUsed = string.IsNullOrWhiteSpace(requestedKey)
            ? GetFallbackKey(errorCode)
            : requestedKey;

        bool? retryable = ParseRetryable(trailers) ?? IsTransientStatus(rpcException.StatusCode);

        if (GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            retryable = true;
        }

        int? retryAfter = ParseRetryAfter(trailers);
        string? correlationId = GetMetadataValue(trailers, GrpcErrorMetadataKeys.CORRELATION_ID);
        string? locale = GetMetadataValue(trailers, GrpcErrorMetadataKeys.LOCALE);

        return new UserFacingError(
            errorCode,
            keyUsed,
            message,
            retryable,
            retryAfter,
            correlationId,
            locale,
            rpcException.StatusCode);
    }

    private static ErrorCode ParseErrorCode(RpcException rpcException, Metadata trailers)
    {
        if (GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            return ErrorCode.DEPENDENCY_UNAVAILABLE;
        }

        string? rawCode = GetMetadataValue(trailers, GrpcErrorMetadataKeys.ERROR_CODE);
        if (!string.IsNullOrWhiteSpace(rawCode) &&
            Enum.TryParse(rawCode, ignoreCase: true, out ErrorCode parsed))
        {
            return parsed;
        }

        return MapStatusCode(rpcException.StatusCode);
    }

    private static string ResolveMessage(ErrorCode errorCode, string statusDetail)
    {
        if (!string.IsNullOrWhiteSpace(statusDetail))
        {
            return statusDetail;
        }

        string defaultMessage = GetDefaultMessage(errorCode);
        return string.IsNullOrWhiteSpace(defaultMessage) ? "An unexpected error occurred" : defaultMessage;
    }

    private static bool? ParseRetryable(Metadata trailers)
    {
        string? retryableValue = GetMetadataValue(trailers, GrpcErrorMetadataKeys.RETRYABLE);
        if (string.IsNullOrWhiteSpace(retryableValue))
        {
            return null;
        }

        return bool.TryParse(retryableValue, out bool parsed) ? parsed : null;
    }

    private static int? ParseRetryAfter(Metadata trailers)
    {
        string? retryAfterValue = GetMetadataValue(trailers, GrpcErrorMetadataKeys.RETRY_AFTER_MILLISECONDS);
        if (string.IsNullOrWhiteSpace(retryAfterValue))
        {
            return null;
        }

        return int.TryParse(retryAfterValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
    }

    private static string? GetMetadataValue(Metadata metadata, string key) => (from entry in metadata
                                                                               where entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                                                                               select entry.Value).FirstOrDefault();

    private static bool IsTransientStatus(StatusCode statusCode) =>
        statusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled;

    private static ErrorCode MapStatusCode(StatusCode statusCode) =>
        statusCode switch
        {
            StatusCode.InvalidArgument or StatusCode.OutOfRange => ErrorCode.VALIDATION_FAILED,
            StatusCode.NotFound => ErrorCode.NOT_FOUND,
            StatusCode.AlreadyExists => ErrorCode.ALREADY_EXISTS,
            StatusCode.PermissionDenied => ErrorCode.PERMISSION_DENIED,
            StatusCode.Unauthenticated => ErrorCode.UNAUTHENTICATED,
            StatusCode.FailedPrecondition => ErrorCode.PRECONDITION_FAILED,
            StatusCode.Aborted => ErrorCode.CONFLICT,
            StatusCode.ResourceExhausted => ErrorCode.RESOURCE_EXHAUSTED,
            StatusCode.Unavailable => ErrorCode.SERVICE_UNAVAILABLE,
            StatusCode.DeadlineExceeded => ErrorCode.DEADLINE_EXCEEDED,
            StatusCode.Cancelled => ErrorCode.CANCELLED,
            _ => ErrorCode.INTERNAL_ERROR
        };

    private static string GetFallbackKey(ErrorCode errorCode) =>
        errorCode switch
        {
            ErrorCode.VALIDATION_FAILED => ErrorI18NKeys.VALIDATION,
            ErrorCode.MAX_ATTEMPTS_REACHED => ErrorI18NKeys.MAX_ATTEMPTS,
            ErrorCode.INVALID_MOBILE_NUMBER => ErrorI18NKeys.INVALID_MOBILE,
            ErrorCode.OTP_EXPIRED => ErrorI18NKeys.OTP_EXPIRED,
            ErrorCode.NOT_FOUND => ErrorI18NKeys.NOT_FOUND,
            ErrorCode.ALREADY_EXISTS => ErrorI18NKeys.ALREADY_EXISTS,
            ErrorCode.UNAUTHENTICATED => ErrorI18NKeys.UNAUTHENTICATED,
            ErrorCode.PERMISSION_DENIED => ErrorI18NKeys.PERMISSION_DENIED,
            ErrorCode.PRECONDITION_FAILED => ErrorI18NKeys.PRECONDITION_FAILED,
            ErrorCode.CONFLICT => ErrorI18NKeys.CONFLICT,
            ErrorCode.RESOURCE_EXHAUSTED => ErrorI18NKeys.RESOURCE_EXHAUSTED,
            ErrorCode.SERVICE_UNAVAILABLE => ErrorI18NKeys.SERVICE_UNAVAILABLE,
            ErrorCode.DEPENDENCY_UNAVAILABLE => ErrorI18NKeys.DEPENDENCY_UNAVAILABLE,
            ErrorCode.DEADLINE_EXCEEDED => ErrorI18NKeys.DEADLINE_EXCEEDED,
            ErrorCode.CANCELLED => ErrorI18NKeys.CANCELLED,
            ErrorCode.DATABASE_UNAVAILABLE => ErrorI18NKeys.DATABASE_UNAVAILABLE,
            _ => ErrorI18NKeys.INTERNAL
        };

    private static string GetDefaultMessage(ErrorCode errorCode) =>
        errorCode switch
        {
            ErrorCode.VALIDATION_FAILED => "Validation failed",
            ErrorCode.MAX_ATTEMPTS_REACHED => "Maximum attempts reached",
            ErrorCode.INVALID_MOBILE_NUMBER => "Invalid mobile number",
            ErrorCode.OTP_EXPIRED => "Verification code expired",
            ErrorCode.NOT_FOUND => "Resource not found",
            ErrorCode.ALREADY_EXISTS => "Already exists",
            ErrorCode.UNAUTHENTICATED => "Authentication required",
            ErrorCode.PERMISSION_DENIED => "Permission denied",
            ErrorCode.PRECONDITION_FAILED => "Precondition failed",
            ErrorCode.CONFLICT => "Conflict detected",
            ErrorCode.RESOURCE_EXHAUSTED => "Resource exhausted",
            ErrorCode.SERVICE_UNAVAILABLE => "Service unavailable",
            ErrorCode.DEPENDENCY_UNAVAILABLE => "Dependency unavailable",
            ErrorCode.DEADLINE_EXCEEDED => "Request timed out",
            ErrorCode.CANCELLED => "Operation cancelled",
            ErrorCode.DATABASE_UNAVAILABLE => "Database unavailable",
            ErrorCode.INTERNAL_ERROR => "Internal error occurred",
            _ => "An unexpected error occurred"
        };

    private static NetworkFailureType DetermineFailureType(RpcException rpcException, UserFacingError userError)
    {
        if (GrpcErrorClassifier.IsIdentityKeyDerivationFailure(rpcException) ||
            GrpcErrorClassifier.IsAuthenticationError(rpcException) ||
            userError.ErrorCode == ErrorCode.UNAUTHENTICATED)
        {
            return NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE;
        }

        if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcException))
        {
            return NetworkFailureType.PROTOCOL_STATE_MISMATCH;
        }

        if (GrpcErrorClassifier.IsServerShutdown(rpcException))
        {
            return NetworkFailureType.DATA_CENTER_SHUTDOWN;
        }

        if (GrpcErrorClassifier.IsBusinessError(rpcException) &&
            !GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            return NetworkFailureType.INVALID_REQUEST_TYPE;
        }

        return NetworkFailureType.DATA_CENTER_NOT_RESPONDING;
    }
}
