using System;
using System.Collections.Generic;
using Ecliptix.Core.Network.Advanced;

namespace Ecliptix.Core.Network.Protocol.Recovery;

public record RestorationResult
{
    public bool Success { get; init; }
    public RestorationStrategy StrategyUsed { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public bool StateWasSynced { get; init; }
    public bool RequiredFreshConnection { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = new();
}