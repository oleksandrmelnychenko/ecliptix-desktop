using System;
using Ecliptix.Core.Network.Advanced;
using Ecliptix.Core.Network.Protocol.Recovery;

namespace Ecliptix.Core.Network.Core.Configuration;

public record StateRestorationConfiguration
{
    public RestorationStrategy PreferredStrategy { get; init; } = RestorationStrategy.Hybrid;
    public TimeSpan LocalStateMaxAge { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan StateSyncTimeout { get; init; } = TimeSpan.FromSeconds(15);
}