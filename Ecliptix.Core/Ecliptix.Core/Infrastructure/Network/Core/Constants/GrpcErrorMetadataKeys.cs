namespace Ecliptix.Core.Infrastructure.Network.Core.Constants;

public static class GrpcErrorMetadataKeys
{
    public const string ErrorCode = "error-code";
    public const string I18nKey = "i18n-key";
    public const string Locale = "locale";
    public const string CorrelationId = "correlation-id";
    public const string Retryable = "retryable";
    public const string RetryAfterMilliseconds = "retry-after-ms";
}
