using System;

namespace Ecliptix.Core.Network.Core.Configuration;

public record OperationQueueConfiguration
{
    public int MaxQueueSize { get; init; } = 1000;
    public int MaxConcurrentOperations { get; init; } = 5;
    public TimeSpan ProcessingInterval { get; init; } = TimeSpan.FromSeconds(1);
    public int MaxRetryAttempts { get; init; } = 5;
    public TimeSpan StaleOperationThreshold { get; init; } = TimeSpan.FromMinutes(15);
}