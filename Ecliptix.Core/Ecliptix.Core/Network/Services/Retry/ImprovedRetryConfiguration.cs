using System;
using System.Collections.Generic;

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

    /// <summary>
    /// Validates the configuration and returns any validation errors.
    /// This ensures that configuration values are sane and will not cause issues at runtime.
    /// </summary>
    /// <returns>A list of validation errors, empty if configuration is valid</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (InitialRetryDelay <= TimeSpan.Zero)
            errors.Add("InitialRetryDelay must be greater than zero");

        if (InitialRetryDelay > TimeSpan.FromMinutes(5))
            errors.Add("InitialRetryDelay should not exceed 5 minutes");

        if (MaxRetryDelay <= TimeSpan.Zero)
            errors.Add("MaxRetryDelay must be greater than zero");

        if (MaxRetryDelay < InitialRetryDelay)
            errors.Add("MaxRetryDelay must be greater than or equal to InitialRetryDelay");

        if (MaxRetryDelay > TimeSpan.FromHours(1))
            errors.Add("MaxRetryDelay should not exceed 1 hour");

        if (MaxRetries < 1)
            errors.Add("MaxRetries must be at least 1");

        if (MaxRetries > 100)
            errors.Add("MaxRetries should not exceed 100 to prevent excessive resource usage");

        if (CircuitBreakerThreshold < 1)
            errors.Add("CircuitBreakerThreshold must be at least 1");

        if (CircuitBreakerThreshold > 50)
            errors.Add("CircuitBreakerThreshold should not exceed 50");

        if (CircuitBreakerDuration <= TimeSpan.Zero)
            errors.Add("CircuitBreakerDuration must be greater than zero");

        if (CircuitBreakerDuration > TimeSpan.FromHours(1))
            errors.Add("CircuitBreakerDuration should not exceed 1 hour");

        if (RequestDeduplicationWindow < TimeSpan.Zero)
            errors.Add("RequestDeduplicationWindow cannot be negative");

        if (RequestDeduplicationWindow > TimeSpan.FromMinutes(5))
            errors.Add("RequestDeduplicationWindow should not exceed 5 minutes");

        if (HealthCheckTimeout <= TimeSpan.Zero)
            errors.Add("HealthCheckTimeout must be greater than zero");

        if (HealthCheckTimeout > TimeSpan.FromMinutes(1))
            errors.Add("HealthCheckTimeout should not exceed 1 minute");

        return errors;
    }

    /// <summary>
    /// Validates the configuration and throws an exception if invalid.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when configuration is invalid</exception>
    public void ValidateAndThrow()
    {
        var errors = Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid retry configuration: {string.Join(", ", errors)}");
        }
    }
}