using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Configuration;
using Ecliptix.Protocol.System.Protocol.ChainStep;
using Ecliptix.Protocol.System.Sodium;

namespace Ecliptix.Protocol.System.Models.Connection;

internal readonly record struct ProtocolConnectionParams(
    uint Id,
    bool IsInitiator,
    SodiumSecureMemoryHandle InitialSendingDh,
    EcliptixProtocolChainStep SendingStep,
    SodiumSecureMemoryHandle PersistentDh,
    byte[] PersistentDhPublic,
    RatchetConfig RatchetConfig,
    PubKeyExchangeType ExchangeType);
