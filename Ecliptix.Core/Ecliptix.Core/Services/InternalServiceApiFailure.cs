using System;

namespace Ecliptix.Core.Services;

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

    public static InternalServiceApiFailure SecureStoreAccessDenied(string details, Exception? inner = null)
    {
        return new InternalServiceApiFailure(InternalServiceApiFailureType.SecureStoreAccessDenied, details, inner);
    }
    
    public static InternalServiceApiFailure ProtocolRecoveryFailed(string details, Exception? inner = null)
    {
        return new InternalServiceApiFailure(InternalServiceApiFailureType.ProtocolRecoveryFailed, details, inner);
    }
    
    public static InternalServiceApiFailure ProtocolStateExpired(string details, Exception? inner = null)
    {
        return new InternalServiceApiFailure(InternalServiceApiFailureType.ProtocolStateExpired, details, inner);
    }

    public static InternalServiceApiFailure SecureStoreNotFound(string details, Exception? inner = null)
    {
        return new InternalServiceApiFailure(InternalServiceApiFailureType.SecureStoreNotFound, details, inner);
    }

    public static InternalServiceApiFailure SecureStoreKeyNotFound(string details, Exception? inner = null)
    {
        return new InternalServiceApiFailure(InternalServiceApiFailureType.SecureStoreKeyNotFound, details, inner);
    }
    
    public static InternalServiceApiFailure ApiRequestFailed(string details, Exception? inner = null)
    {
        return new InternalServiceApiFailure(InternalServiceApiFailureType.ApiRequestFailed, details, inner);
    }
}