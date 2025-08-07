using System;

namespace Ecliptix.Core.Network.Advanced;

public record StateRestorationConfiguration
{
    public RestorationStrategy PreferredStrategy { get; init; } = RestorationStrategy.Hybrid;
    public TimeSpan LocalStateMaxAge { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan StateSyncTimeout { get; init; } = TimeSpan.FromSeconds(15);
}