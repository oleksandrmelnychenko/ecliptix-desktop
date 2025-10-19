using System;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed record RetryPolicy(
    bool RetryTransient,
    bool ReinitOnCompleteFailure,
    int MaxAttempts,
    int BaseDelayMs,
    int MaxDelayMs,
    bool UseJitter)
{
    public static RetryPolicy NoRetry { get; } = new(
        RetryTransient: false,
        ReinitOnCompleteFailure: false,
        MaxAttempts: 1,
        BaseDelayMs: 0,
        MaxDelayMs: 0,
        UseJitter: false);

    public static RetryPolicy CreateTransientPolicy(
        int maxAttempts,
        int baseDelayMs,
        int maxDelayMs,
        bool useJitter,
        bool reinitOnCompleteFailure = false)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (baseDelayMs < 0) throw new ArgumentOutOfRangeException(nameof(baseDelayMs));
        if (maxDelayMs < baseDelayMs) throw new ArgumentOutOfRangeException(nameof(maxDelayMs));

        return new RetryPolicy(
            RetryTransient: true,
            ReinitOnCompleteFailure: reinitOnCompleteFailure,
            MaxAttempts: maxAttempts,
            BaseDelayMs: baseDelayMs,
            MaxDelayMs: maxDelayMs,
            UseJitter: useJitter);
    }
}
