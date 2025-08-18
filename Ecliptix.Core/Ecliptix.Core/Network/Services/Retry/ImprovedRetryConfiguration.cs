using System;
using System.Collections.Generic;

namespace Ecliptix.Core.Network.Services.Retry;

public class ImprovedRetryConfiguration
{
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(2);
    public int MaxRetries { get; init; } = 10;
    public bool UseAdaptiveRetry { get; init; } = true;

    public static ImprovedRetryConfiguration Production => new()
    {
        InitialRetryDelay = TimeSpan.FromSeconds(5),
        MaxRetryDelay = TimeSpan.FromMinutes(2),
        MaxRetries = 10,
        UseAdaptiveRetry = true
    };

    public static ImprovedRetryConfiguration Development => new()
    {
        InitialRetryDelay = TimeSpan.FromSeconds(2),
        MaxRetryDelay = TimeSpan.FromSeconds(30),
        MaxRetries = 10,
        UseAdaptiveRetry = false
    };

    public List<string> Validate()
    {
        List<string> errors = [];

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

        return errors;
    }

    public void ValidateAndThrow()
    {
        List<string> errors = Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException($"Invalid retry configuration: {string.Join(", ", errors)}");
        }
    }
}