using System;
using System.Globalization;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Core.Constants;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

internal sealed class GrpcErrorProcessor(ILocalizationService localizationService) : IGrpcErrorProcessor
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
            NetworkFailureType.INVALID_REQUEST_TYPE => NetworkFailure.InvalidRequestType(userError.Message, rpcException,
                userError),
            NetworkFailureType.DATA_CENTER_SHUTDOWN => NetworkFailure.DataCenterShutdown(userError.Message, rpcException,
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
        (string message, string keyUsed) = ResolveMessage(
            GetMetadataValue(trailers, GrpcErrorMetadataKeys.I_18N_KEY),
            errorCode,
            rpcException.Status.Detail);

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

    private ErrorCode ParseErrorCode(RpcException rpcException, Metadata trailers)
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

    private (string Message, string KeyUsed) ResolveMessage(string? requestedKey, ErrorCode errorCode,
        string statusDetail)
    {
        if (!string.IsNullOrWhiteSpace(requestedKey))
        {
            string localized = Localize(requestedKey);
            if (!IsMissing(localized))
            {
                return (localized, requestedKey);
            }
        }

        string fallbackKey = GetFallbackKey(errorCode);
        string fallbackMessage = Localize(fallbackKey);
        if (!IsMissing(fallbackMessage))
        {
            return (fallbackMessage, fallbackKey);
        }

        if (!string.IsNullOrWhiteSpace(statusDetail))
        {
            return (statusDetail, fallbackKey);
        }

        string internalMessage = Localize(ErrorI18NKeys.INTERNAL);
        if (IsMissing(internalMessage))
        {
            internalMessage = "An unexpected error occurred";
        }

        return (internalMessage, ErrorI18NKeys.INTERNAL);
    }

    private string Localize(string key)
    {
        try
        {
            return localizationService[key];
        }
        catch
        {
            return $"!{key}!";
        }
    }

    private static bool IsMissing(string value)
    {
        return string.IsNullOrWhiteSpace(value) ||
               value.StartsWith("!", StringComparison.Ordinal) && value.EndsWith("!", StringComparison.Ordinal);
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

    private static string? GetMetadataValue(Metadata metadata, string key)
    {
        foreach (Metadata.Entry entry in metadata)
        {
            if (entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        return null;
    }

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

        if (GrpcErrorClassifier.IsTransientInfrastructure(rpcException) ||
            GrpcErrorClassifier.RequiresHandshakeRecovery(rpcException) ||
            GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            return NetworkFailureType.DATA_CENTER_NOT_RESPONDING;
        }

        if (rpcException.StatusCode == StatusCode.Cancelled)
        {
        }

        return NetworkFailureType.DATA_CENTER_NOT_RESPONDING;
    }
}
