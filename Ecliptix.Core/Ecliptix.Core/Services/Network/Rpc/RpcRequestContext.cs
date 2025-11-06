using System;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class RpcRequestContext(string correlationId, string idempotencyKey, int attempt = 1)
{
    public string CorrelationId { get; } = correlationId;

    public string IdempotencyKey { get; } = idempotencyKey;

    public int Attempt { get; } = attempt;

    public bool ReinitAttempted { get; private set; }

    public static RpcRequestContext CreateNew(int attempt = 1)
    {
        return new RpcRequestContext(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            attempt);
    }

    public static RpcRequestContext CreateNewWithStableKey(string stableIdempotencyKey, int attempt = 1)
    {
        return new RpcRequestContext(
            Guid.NewGuid().ToString("N"),
            stableIdempotencyKey,
            attempt);
    }

    public void MarkReinitAttempted() => ReinitAttempted = true;
}
