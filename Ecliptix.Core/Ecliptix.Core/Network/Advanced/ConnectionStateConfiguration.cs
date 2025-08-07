using System;

namespace Ecliptix.Core.Network.Advanced;

public record ConnectionStateConfiguration
{
    public TimeSpan HealthCheckInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxUnhealthyDuration { get; init; } = TimeSpan.FromMinutes(5);
    public int MaxConsecutiveFailures { get; init; } = 5;
    public double MinimumSuccessRate { get; init; } = 0.8;
    public bool AutoRecoveryEnabled { get; init; } = true;
    public TimeSpan AutoRecoveryInterval { get; init; } = TimeSpan.FromMinutes(1);
}