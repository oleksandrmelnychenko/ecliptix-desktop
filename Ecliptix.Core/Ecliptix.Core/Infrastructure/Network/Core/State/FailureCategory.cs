namespace Ecliptix.Core.Infrastructure.Network.Core.State;

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