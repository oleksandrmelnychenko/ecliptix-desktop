using System;

namespace Ecliptix.Core.Infrastructure.Network.Core.State.Configuration;

public record ConnectionStateConfiguration
{
    public TimeSpan HealthCheckInterval { get; } = TimeSpan.FromSeconds(30);
    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxUnhealthyDuration { get; } = TimeSpan.FromMinutes(5);
    public static int MaxConsecutiveFailures => 5;
    public static double MinimumSuccessRate => 0.8;
    public static bool AutoRecoveryEnabled => true;
    public TimeSpan AutoRecoveryInterval { get; } = TimeSpan.FromMinutes(1);
}