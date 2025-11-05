using System;

namespace Ecliptix.Core.Services.Common;

public class InternalServiceApiFailure
{
    private InternalServiceApiFailure(InternalServiceApiFailureType type, string message,
        Exception? innerException = null)
    {
        Type = type;
        Message = message;
        InnerException = innerException;
    }

    public InternalServiceApiFailureType Type { get; }
    public string Message { get; }
    public Exception? InnerException { get; }

    public static InternalServiceApiFailure SecureStoreAccessDenied(string details, Exception? inner = null) =>
        new(InternalServiceApiFailureType.SECURE_STORE_ACCESS_DENIED, details, inner);

    public static InternalServiceApiFailure SecureStoreKeyNotFound(string details, Exception? inner = null) =>
        new(InternalServiceApiFailureType.SECURE_STORE_KEY_NOT_FOUND, details, inner);

    public static InternalServiceApiFailure ApiRequestFailed(string details, Exception? inner = null) =>
        new(InternalServiceApiFailureType.API_REQUEST_FAILED, details, inner);
}
