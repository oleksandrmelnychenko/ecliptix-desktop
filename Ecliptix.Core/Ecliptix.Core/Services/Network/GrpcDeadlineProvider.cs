using System;
using System.Threading;
using Ecliptix.Core.Services.Network.Rpc;
using Serilog;

namespace Ecliptix.Core.Services.Network;

internal sealed class GrpcDeadlineProvider(IOperationTimeoutProvider timeoutProvider) : IGrpcDeadlineProvider
{
    private static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(30);

    public DateTime GetDeadlineUtc(RpcServiceType serviceType, RpcRequestContext? requestContext)
    {
        TimeSpan operationTimeout = timeoutProvider.GetTimeout(serviceType, requestContext);
        TimeSpan normalizedTimeout = NormalizeTimeout(operationTimeout);
        DateTime now = DateTime.UtcNow;

        try
        {
            return now.Add(normalizedTimeout);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Log.Warning(ex, "[GRPC-DEADLINE] Timeout value {NormalizedTimeout} caused overflow for service {ServiceType}. Using max deadline. CorrelationId: {CorrelationId}",
                normalizedTimeout,
                serviceType,
                requestContext?.CorrelationId ?? "N/A");
            return DateTime.SpecifyKind(DateTime.MaxValue.AddTicks(-1), DateTimeKind.Utc);
        }
    }

    private static TimeSpan NormalizeTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            return DefaultDeadline;
        }

        return timeout;
    }
}
