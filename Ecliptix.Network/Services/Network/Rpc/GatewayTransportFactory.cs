using Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.Common;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;

namespace Ecliptix.Network.Services.Network.Rpc;

internal static class GatewayTransportFactory
{
    public static EventEnvelope BuildEnvelope(
        GatewayRoute route,
        IMessage payload,
        IRpcMetaDataProvider metaDataProvider,
        RpcRequestContext? requestContext,
        PubKeyExchangeType exchangeType = PubKeyExchangeType.DataCenterEphemeralConnect)
    {
        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        if (metaDataProvider == null)
        {
            throw new ArgumentNullException(nameof(metaDataProvider));
        }

        if (route == null)
        {
            throw new ArgumentNullException(nameof(route));
        }

        EventMetadata metadata = new()
        {
            Identity = new EventIdentity
            {
                EventId = Guid.NewGuid().ToString("N"),
                EventType = route.EventType,
                Context = route.Context,
                CorrelationId = requestContext?.CorrelationId ?? string.Empty,
                DeliveryKind = route.DeliveryKind
            },
            Client = new ClientContext
            {
                Locale = metaDataProvider.Culture ?? string.Empty,
                ApplicationInstanceId = ByteString.CopyFrom(metaDataProvider.AppInstanceId.ToByteArray()),
                DeviceId = ByteString.CopyFrom(metaDataProvider.DeviceId.ToByteArray()),
                IdempotencyKey = requestContext?.IdempotencyKey ?? string.Empty,
                Platform = metaDataProvider.Platform
            },
            Security = new SecurityContext
            {
                KeyExchangeContext = exchangeType.ToString()
            }
        };

        return new EventEnvelope
        {
            Metadata = metadata,
            Payload = ByteString.CopyFrom(payload.ToByteArray())
        };
    }

    public static NetworkFailure? MapOutcome(EventMetadata? metadata)
    {
        if (metadata?.Outcome == null)
        {
            return null;
        }

        if (!string.Equals(metadata.Outcome.Status, "ERR", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string code = metadata.Outcome.ErrorCode ?? "UNKNOWN";
        return NetworkFailure.DataCenterNotResponding($"Transport reported error: {code}");
    }
}
