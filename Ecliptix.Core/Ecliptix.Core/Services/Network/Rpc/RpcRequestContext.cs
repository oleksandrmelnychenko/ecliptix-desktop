using System;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class RpcRequestContext
{
    public RpcRequestContext(string correlationId, string idempotencyKey, int attempt = 1)
    {
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        IdempotencyKey = idempotencyKey ?? throw new ArgumentNullException(nameof(idempotencyKey));
        Attempt = attempt;
    }

    public string CorrelationId { get; }

    public string IdempotencyKey { get; }

    public int Attempt { get; }

    public bool ReinitAttempted { get; private set; }

    public static RpcRequestContext CreateNew(int attempt = 1)
    {
        return new RpcRequestContext(
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            attempt);
    }

    public RpcRequestContext CreateNextAttempt()
    {
        return CreateNew(Attempt + 1);
    }

    public void MarkReinitAttempted()
    {
        ReinitAttempted = true;
    }
}
