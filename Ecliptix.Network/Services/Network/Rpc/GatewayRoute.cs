using Ecliptix.Protobuf.Transport.Common;

namespace Ecliptix.Network.Services.Network.Rpc;

internal sealed record GatewayRoute(string Context, string EventType, DeliveryKind DeliveryKind);
