namespace Ecliptix.Core.Services.Network.Rpc;

public sealed record RetryBehavior
{
    public required bool ShouldRetry { get; init; }
    public required int MAX_ATTEMPTS { get; init; }
    public required bool ReinitOnCompleteFailure { get; init; }

    public static RetryBehavior NoRetry { get; } = new()
    {
        ShouldRetry = false,
        MAX_ATTEMPTS = 1,
        ReinitOnCompleteFailure = false
    };
}
