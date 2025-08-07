namespace Ecliptix.Core.Network.Core;

public enum FailureCategory
{
    Unknown,
    NetworkConnectivity,
    ServerError,
    Authentication,
    RateLimit,
    Protocol,
    Timeout,
    CryptographicDesync
}