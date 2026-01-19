using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Infrastructure.Network.Abstractions.Transport;

public interface IOutboundSink
{
    Task<Result<Unit, NetworkFailure>> SendAsync(SecureEnvelope envelope);
}
