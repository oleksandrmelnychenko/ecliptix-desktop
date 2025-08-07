namespace Ecliptix.Core.Network.Advanced;

public enum FailureCategory
{
    Unknown,
    NetworkConnectivity,
    ServerError,
    Authentication,
    RateLimit,
    Protocol,
    Timeout,
    Configuration,
    CryptographicDesync
}