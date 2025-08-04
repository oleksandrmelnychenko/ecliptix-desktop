namespace Ecliptix.Core.Services;

public enum InternalServiceApiFailureType
{
    SecureStoreNotFound,
    SecureStoreKeyNotFound,
    SecureStoreAccessDenied,
    ApiRequestFailed,
    ProtocolStateExpired,
    ProtocolRecoveryFailed,
}