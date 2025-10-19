using System;

namespace Ecliptix.Core.Services.Network.Resilience;

public sealed record RetryExecutionOptions(
    TimeSpan BaseDelay,
    TimeSpan MaxDelay,
    bool UseJitter);
