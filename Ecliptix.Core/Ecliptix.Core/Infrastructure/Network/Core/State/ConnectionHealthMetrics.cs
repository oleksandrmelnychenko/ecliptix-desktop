using System;
using System.Collections.Generic;
using Ecliptix.Core.Services.Network.Infrastructure.Queue;

namespace Ecliptix.Core.Infrastructure.Network.Core.State;

public record ConnectionHealthMetrics
{
    public int ConsecutiveSuccesses { get; init; }
    public int ConsecutiveFailures { get; init; }
    public TimeSpan AverageLatency { get; init; } = TimeSpan.Zero;
    public double SuccessRate { get; init; } = 1.0;
    public Dictionary<OperationType, DateTime> LastOperationTimes { get; init; } = new();
    public Dictionary<FailureCategory, int> FailureCounts { get; init; } = new();
}
