using System;
using System.Collections.Generic;

namespace Ecliptix.Core.Network.Advanced;

public record ConnectionHealthMetrics
{
    public int ConsecutiveSuccesses { get; init; }
    public int ConsecutiveFailures { get; init; }
    public TimeSpan AverageLatency { get; init; } = TimeSpan.Zero;
    public double SuccessRate { get; init; } = 1.0;
    public Dictionary<OperationType, DateTime> LastOperationTimes { get; init; } = new();
    public Dictionary<FailureCategory, int> FailureCounts { get; init; } = new();
}