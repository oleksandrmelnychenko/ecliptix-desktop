using System;

namespace Ecliptix.Core.Network.Services.Retry;

public class ImprovedRetryConfiguration
{
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(2);
    public int MaxRetries { get; set; } = 10;
    public int CircuitBreakerThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RequestDeduplicationWindow { get; set; } = TimeSpan.FromSeconds(10);
    public bool UseAdaptiveRetry { get; set; } = true;
    public TimeSpan HealthCheckTimeout { get; set; } = TimeSpan.FromSeconds(5);
    
    public static ImprovedRetryConfiguration Production => new()
    {
        InitialRetryDelay = TimeSpan.FromSeconds(5),
        MaxRetryDelay = TimeSpan.FromMinutes(2),
        MaxRetries = 10,
        CircuitBreakerThreshold = 5,
        CircuitBreakerDuration = TimeSpan.FromMinutes(1),
        UseAdaptiveRetry = true
    };
    
    public static ImprovedRetryConfiguration Development => new()
    {
        InitialRetryDelay = TimeSpan.FromSeconds(2),
        MaxRetryDelay = TimeSpan.FromSeconds(30),
        MaxRetries = 10,
        CircuitBreakerThreshold = 3,
        CircuitBreakerDuration = TimeSpan.FromSeconds(30),
        UseAdaptiveRetry = false
    };
}