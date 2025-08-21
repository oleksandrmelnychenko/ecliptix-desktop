using System;
using System.Collections.Generic;

namespace Ecliptix.Core.Infrastructure.Network.Core.State;

public record ConnectionHealthMetrics
{
    public int ConsecutiveSuccesses { get; init; }
    public int ConsecutiveFailures { get; init; }
    public TimeSpan AverageLatency { get; init; } = TimeSpan.Zero;
    public double SuccessRate { get; init; } = 1.0;
    public Dictionary<FailureCategory, int> FailureCounts { get; init; } = new();
}
