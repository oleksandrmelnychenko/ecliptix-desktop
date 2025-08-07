using System.Threading.Tasks;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Transport;

public interface IOutboundSink
{
    Task<Result<Unit, NetworkFailure>> SendAsync(CipherPayload payload);
}