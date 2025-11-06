using System.Threading;
using Ecliptix.Protobuf.Protocol;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

internal readonly record struct SecrecyChannelRequest(
    uint ConnectId,
    PubKeyExchangeType ExchangeType,
    int? MaxRetries,
    bool SaveState,
    bool EnablePendingRegistration,
    CancellationToken CancellationToken);
