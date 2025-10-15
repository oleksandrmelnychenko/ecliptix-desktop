using System;

namespace Ecliptix.Core.Services.Network.Resilience;

public class RetryStrategyConfiguration
{
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxRetries { get; init; } = 10;
    public bool UseAdaptiveRetry { get; init; } = true;
    public TimeSpan PerAttemptTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
