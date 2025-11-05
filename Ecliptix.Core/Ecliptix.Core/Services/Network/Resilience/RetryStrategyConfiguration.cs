using System;

namespace Ecliptix.Core.Services.Network.Resilience;

public class RetryStrategyConfiguration
{
    public TimeSpan INITIAL_RETRY_DELAY { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MAX_RETRY_DELAY { get; init; } = TimeSpan.FromMinutes(2);
    public int MAX_RETRIES { get; init; } = 10;
    public bool USE_ADAPTIVE_RETRY { get; init; } = true;
    public TimeSpan PER_ATTEMPT_TIMEOUT { get; init; } = TimeSpan.FromSeconds(30);
}
