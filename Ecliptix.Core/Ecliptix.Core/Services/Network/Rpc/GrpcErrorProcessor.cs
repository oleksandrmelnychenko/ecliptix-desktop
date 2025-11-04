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
            NetworkFailureType.InvalidRequestType => NetworkFailure.InvalidRequestType(userError.Message, rpcException,
                userError),
            NetworkFailureType.DataCenterShutdown => NetworkFailure.DataCenterShutdown(userError.Message, rpcException,
                userError),
            NetworkFailureType.ProtocolStateMismatch => NetworkFailure.ProtocolStateMismatch(userError.Message,
                rpcException, userError),
            NetworkFailureType.CriticalAuthenticationFailure => NetworkFailure.CriticalAuthenticationFailure(
                userError.Message, rpcException, userError),
            NetworkFailureType.OperationCancelled => NetworkFailure.OperationCancelled(userError.Message, rpcException,
                userError),
            _ => NetworkFailure.DataCenterNotResponding(userError.Message, rpcException, userError)
        };
    }

    private UserFacingError CreateUserFacingError(RpcException rpcException)
    {
        Metadata trailers = rpcException.Trailers;

        ERROR_CODE errorCode = ParseErrorCode(rpcException, trailers);
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

    private ERROR_CODE ParseErrorCode(RpcException rpcException, Metadata trailers)
    {
        if (GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            return ERROR_CODE.DEPENDENCY_UNAVAILABLE;
        }

        string? rawCode = GetMetadataValue(trailers, GrpcErrorMetadataKeys.ERROR_CODE);
        if (!string.IsNullOrWhiteSpace(rawCode) &&
            Enum.TryParse(rawCode, ignoreCase: true, out ERROR_CODE parsed))
        {
            return parsed;
        }

        return MapStatusCode(rpcException.StatusCode);
    }

    private (string Message, string KeyUsed) ResolveMessage(string? requestedKey, ERROR_CODE errorCode,
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

        string internalMessage = Localize(ErrorI18nKeys.INTERNAL);
        if (IsMissing(internalMessage))
        {
            internalMessage = "An unexpected error occurred";
        }

        return (internalMessage, ErrorI18nKeys.INTERNAL);
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

    private static ERROR_CODE MapStatusCode(StatusCode statusCode) =>
        statusCode switch
        {
            StatusCode.InvalidArgument or StatusCode.OutOfRange => ERROR_CODE.ValidationFailed,
            StatusCode.NotFound => ERROR_CODE.NOT_FOUND,
            StatusCode.AlreadyExists => ERROR_CODE.ALREADY_EXISTS,
            StatusCode.PermissionDenied => ERROR_CODE.PERMISSION_DENIED,
            StatusCode.Unauthenticated => ERROR_CODE.UNAUTHENTICATED,
            StatusCode.FailedPrecondition => ERROR_CODE.PRECONDITION_FAILED,
            StatusCode.Aborted => ERROR_CODE.CONFLICT,
            StatusCode.ResourceExhausted => ERROR_CODE.RESOURCE_EXHAUSTED,
            StatusCode.Unavailable => ERROR_CODE.SERVICE_UNAVAILABLE,
            StatusCode.DeadlineExceeded => ERROR_CODE.DEADLINE_EXCEEDED,
            StatusCode.Cancelled => ERROR_CODE.CANCELLED,
            _ => ERROR_CODE.InternalError
        };

    private static string GetFallbackKey(ERROR_CODE errorCode) =>
        errorCode switch
        {
            ERROR_CODE.ValidationFailed => ErrorI18nKeys.VALIDATION,
            ERROR_CODE.MaxAttemptsReached => ErrorI18nKeys.MAX_ATTEMPTS,
            ERROR_CODE.InvalidMobileNumber => ErrorI18nKeys.INVALID_MOBILE,
            ERROR_CODE.OTP_EXPIRED => ErrorI18nKeys.OTP_EXPIRED,
            ERROR_CODE.NOT_FOUND => ErrorI18nKeys.NOT_FOUND,
            ERROR_CODE.ALREADY_EXISTS => ErrorI18nKeys.ALREADY_EXISTS,
            ERROR_CODE.UNAUTHENTICATED => ErrorI18nKeys.UNAUTHENTICATED,
            ERROR_CODE.PERMISSION_DENIED => ErrorI18nKeys.PERMISSION_DENIED,
            ERROR_CODE.PRECONDITION_FAILED => ErrorI18nKeys.PRECONDITION_FAILED,
            ERROR_CODE.CONFLICT => ErrorI18nKeys.CONFLICT,
            ERROR_CODE.RESOURCE_EXHAUSTED => ErrorI18nKeys.RESOURCE_EXHAUSTED,
            ERROR_CODE.SERVICE_UNAVAILABLE => ErrorI18nKeys.SERVICE_UNAVAILABLE,
            ERROR_CODE.DEPENDENCY_UNAVAILABLE => ErrorI18nKeys.DEPENDENCY_UNAVAILABLE,
            ERROR_CODE.DEADLINE_EXCEEDED => ErrorI18nKeys.DEADLINE_EXCEEDED,
            ERROR_CODE.CANCELLED => ErrorI18nKeys.CANCELLED,
            ERROR_CODE.DATABASE_UNAVAILABLE => ErrorI18nKeys.DATABASE_UNAVAILABLE,
            _ => ErrorI18nKeys.INTERNAL
        };

    private static NetworkFailureType DetermineFailureType(RpcException rpcException, UserFacingError userError)
    {
        if (GrpcErrorClassifier.IsIdentityKeyDerivationFailure(rpcException) ||
            GrpcErrorClassifier.IsAuthenticationError(rpcException) ||
            userError.ERROR_CODE == ERROR_CODE.UNAUTHENTICATED)
        {
            return NetworkFailureType.CriticalAuthenticationFailure;
        }

        if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcException))
        {
            return NetworkFailureType.ProtocolStateMismatch;
        }

        if (GrpcErrorClassifier.IsServerShutdown(rpcException))
        {
            return NetworkFailureType.DataCenterShutdown;
        }

        if (GrpcErrorClassifier.IsBusinessError(rpcException) &&
            !GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            return NetworkFailureType.InvalidRequestType;
        }

        if (GrpcErrorClassifier.IsTransientInfrastructure(rpcException) ||
            GrpcErrorClassifier.RequiresHandshakeRecovery(rpcException) ||
            GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            return NetworkFailureType.DataCenterNotResponding;
        }

        if (rpcException.StatusCode == StatusCode.Cancelled)
        {
        }

        return NetworkFailureType.DataCenterNotResponding;
    }
}
