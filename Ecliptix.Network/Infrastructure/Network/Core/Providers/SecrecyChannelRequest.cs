using Ecliptix.Protobuf.Protocol;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

internal readonly record struct SecrecyChannelRequest(
    uint ConnectId,
    PubKeyExchangeType ExchangeType,
    int? MaxRetries,
    bool SaveState,
    bool EnablePendingRegistration,
    CancellationToken CancellationToken);
