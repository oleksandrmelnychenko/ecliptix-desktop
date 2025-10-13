using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;

namespace Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;

public interface INetworkProvider
{
    uint ComputeUniqueConnectId(PubKeyExchangeType pubKeyExchangeType);
}
