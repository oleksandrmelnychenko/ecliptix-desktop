namespace Ecliptix.Core.Infrastructure.Network.Core.Constants;

public static class GrpcErrorMetadataKeys
{
    public const string ERROR_CODE = "error-code";
    public const string I_18_N_KEY = "i18n-key";
    public const string LOCALE = "locale";
    public const string CORRELATION_ID = "correlation-id";
    public const string RETRYABLE = "retryable";
    public const string RETRY_AFTER_MILLISECONDS = "retry-after-ms";
}
