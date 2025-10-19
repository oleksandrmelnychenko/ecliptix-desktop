using System;
using System.Globalization;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Constants;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

internal sealed class GrpcErrorProcessor : IGrpcErrorProcessor
{
    private readonly ILocalizationService _localizationService;

    public GrpcErrorProcessor(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    public NetworkFailure Process(RpcException rpcException)
    {
        return CreateFailure(rpcException);
    }

    public Task<NetworkFailure> ProcessAsync(RpcException rpcException, INetworkEventService networkEvents)
    {
        return Task.FromResult(CreateFailure(rpcException));
    }

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

        ErrorCode errorCode = ParseErrorCode(rpcException, trailers);
        (string message, string keyUsed) = ResolveMessage(
            GetMetadataValue(trailers, GrpcErrorMetadataKeys.I18nKey),
            errorCode,
            rpcException.Status.Detail);

        bool? retryable = ParseRetryable(trailers);
        if (retryable is null)
        {
            retryable = IsTransientStatus(rpcException.StatusCode);
        }

        if (GrpcErrorClassifier.IsAuthFlowMissing(rpcException))
        {
            retryable = true;
        }

        int? retryAfter = ParseRetryAfter(trailers);
        string? correlationId = GetMetadataValue(trailers, GrpcErrorMetadataKeys.CorrelationId);
        string? locale = GetMetadataValue(trailers, GrpcErrorMetadataKeys.Locale);

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
            return ErrorCode.DependencyUnavailable;
        }

        string? rawCode = GetMetadataValue(trailers, GrpcErrorMetadataKeys.ErrorCode);
        if (!string.IsNullOrWhiteSpace(rawCode) &&
            Enum.TryParse(rawCode, ignoreCase: true, out ErrorCode parsed))
        {
            return parsed;
        }

        return MapStatusCode(rpcException.StatusCode);
    }

    private (string Message, string KeyUsed) ResolveMessage(string? requestedKey, ErrorCode errorCode, string statusDetail)
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

        string internalMessage = Localize(ErrorI18nKeys.Internal);
        if (IsMissing(internalMessage))
        {
            internalMessage = "An unexpected error occurred";
        }

        return (internalMessage, ErrorI18nKeys.Internal);
    }

    private string Localize(string key)
    {
        try
        {
            return _localizationService[key];
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
        string? retryableValue = GetMetadataValue(trailers, GrpcErrorMetadataKeys.Retryable);
        if (string.IsNullOrWhiteSpace(retryableValue))
        {
            return null;
        }

        return bool.TryParse(retryableValue, out bool parsed) ? parsed : null;
    }

    private static int? ParseRetryAfter(Metadata trailers)
    {
        string? retryAfterValue = GetMetadataValue(trailers, GrpcErrorMetadataKeys.RetryAfterMilliseconds);
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
            StatusCode.InvalidArgument or StatusCode.OutOfRange => ErrorCode.ValidationFailed,
            StatusCode.NotFound => ErrorCode.NotFound,
            StatusCode.AlreadyExists => ErrorCode.AlreadyExists,
            StatusCode.PermissionDenied => ErrorCode.PermissionDenied,
            StatusCode.Unauthenticated => ErrorCode.Unauthenticated,
            StatusCode.FailedPrecondition => ErrorCode.PreconditionFailed,
            StatusCode.Aborted => ErrorCode.Conflict,
            StatusCode.ResourceExhausted => ErrorCode.ResourceExhausted,
            StatusCode.Unavailable => ErrorCode.ServiceUnavailable,
            StatusCode.DeadlineExceeded => ErrorCode.DeadlineExceeded,
            StatusCode.Cancelled => ErrorCode.Cancelled,
            _ => ErrorCode.InternalError
        };

    private static string GetFallbackKey(ErrorCode errorCode) =>
        errorCode switch
        {
            ErrorCode.ValidationFailed => ErrorI18nKeys.Validation,
            ErrorCode.MaxAttemptsReached => ErrorI18nKeys.MaxAttempts,
            ErrorCode.InvalidMobileNumber => ErrorI18nKeys.InvalidMobile,
            ErrorCode.OtpExpired => ErrorI18nKeys.OtpExpired,
            ErrorCode.NotFound => ErrorI18nKeys.NotFound,
            ErrorCode.AlreadyExists => ErrorI18nKeys.AlreadyExists,
            ErrorCode.Unauthenticated => ErrorI18nKeys.Unauthenticated,
            ErrorCode.PermissionDenied => ErrorI18nKeys.PermissionDenied,
            ErrorCode.PreconditionFailed => ErrorI18nKeys.PreconditionFailed,
            ErrorCode.Conflict => ErrorI18nKeys.Conflict,
            ErrorCode.ResourceExhausted => ErrorI18nKeys.ResourceExhausted,
            ErrorCode.ServiceUnavailable => ErrorI18nKeys.ServiceUnavailable,
            ErrorCode.DependencyUnavailable => ErrorI18nKeys.DependencyUnavailable,
            ErrorCode.DeadlineExceeded => ErrorI18nKeys.DeadlineExceeded,
            ErrorCode.Cancelled => ErrorI18nKeys.Cancelled,
            ErrorCode.DatabaseUnavailable => ErrorI18nKeys.DatabaseUnavailable,
            _ => ErrorI18nKeys.Internal
        };

    private static NetworkFailureType DetermineFailureType(RpcException rpcException, UserFacingError userError)
    {
        if (GrpcErrorClassifier.IsIdentityKeyDerivationFailure(rpcException) ||
            GrpcErrorClassifier.IsAuthenticationError(rpcException) ||
            userError.ErrorCode == ErrorCode.Unauthenticated)
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
            return NetworkFailureType.DataCenterNotResponding;
        }

        return NetworkFailureType.DataCenterNotResponding;
    }
}
