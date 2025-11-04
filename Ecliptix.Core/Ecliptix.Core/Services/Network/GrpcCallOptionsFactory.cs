using System;
using System.Globalization;
using System.Threading;
using Ecliptix.Core.Services.Network.Rpc;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network;

internal sealed class GrpcCallOptionsFactory(IGrpcDeadlineProvider deadlineProvider) : IGrpcCallOptionsFactory
{
    public CallOptions Create(
        RpcServiceType serviceType,
        RpcRequestContext? requestContext,
        CancellationToken cancellationToken,
        Metadata? additionalHeaders = null)
    {
        Metadata? headers = MergeHeaders(BuildHeaders(requestContext), additionalHeaders);
        DateTime deadlineUtc = deadlineProvider.GetDeadlineUtc(serviceType, requestContext);
        return new CallOptions(headers, deadline: deadlineUtc, cancellationToken: cancellationToken);
    }

    private static Metadata? BuildHeaders(RpcRequestContext? requestContext)
    {
        if (requestContext == null)
        {
            return null;
        }

        Metadata headers = new()
        {
            { "x-correlation-id", requestContext.CORRELATION_ID },
            { "x-idempotency-key", requestContext.IdempotencyKey },
            { "x-attempt", requestContext.Attempt.ToString(CultureInfo.InvariantCulture) }
        };

        if (requestContext.ReinitAttempted)
        {
            headers.Add("x-reinit-attempted", "true");
        }

        return headers;
    }

    private static Metadata? MergeHeaders(Metadata? baseHeaders, Metadata? additionalHeaders)
    {
        if (additionalHeaders == null || additionalHeaders.Count == 0)
        {
            return baseHeaders;
        }

        Metadata merged = [];

        if (baseHeaders != null)
        {
            foreach (Metadata.Entry entry in baseHeaders)
            {
                AddEntry(merged, entry);
            }
        }

        foreach (Metadata.Entry entry in additionalHeaders)
        {
            AddEntry(merged, entry);
        }

        return merged;
    }

    private static void AddEntry(Metadata target, Metadata.Entry entry)
    {
        if (entry.IsBinary)
        {
            target.Add(entry.Key, entry.ValueBytes);
        }
        else
        {
            target.Add(entry.Key, entry.Value);
        }
    }
}
