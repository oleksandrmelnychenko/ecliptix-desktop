using System;
using System.Collections.Generic;

namespace Ecliptix.Core.Infrastructure.Network.Core.Connectivity;

public record InternetConnectivityObserverOptions
{
    public static InternetConnectivityObserverOptions Default { get; } = new();

    public IReadOnlyList<string> ProbeUrls { get; init; } = [
        "https://www.google.com/generate_204",
        "http://www.gstatic.com/generate_204"
    ];

    public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public int FailureThreshold { get; init; } = 3;

    public int SuccessThreshold { get; init; } = 1;
}