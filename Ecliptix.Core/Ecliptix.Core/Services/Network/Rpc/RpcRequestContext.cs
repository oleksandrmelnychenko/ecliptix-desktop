using System;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class RpcRequestContext(string correlationId, string idempotencyKey, int attempt = 1)
{
    public string CorrelationId { get; } = correlationId ?? throw new ArgumentNullException(nameof(correlationId));

    public string IdempotencyKey { get; } = idempotencyKey ?? throw new ArgumentNullException(nameof(idempotencyKey));

    public int Attempt { get; } = attempt;

    public bool ReinitAttempted { get; private set; }

    public static RpcRequestContext CreateNew(int attempt = 1)
    {
        return new RpcRequestContext(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            attempt);
    }

    public void MarkReinitAttempted()
    {
        ReinitAttempted = true;
    }
}
