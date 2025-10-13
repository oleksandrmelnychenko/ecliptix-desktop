using System.Threading.Tasks;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;

public interface IOutboundSink
{
    Task<Result<Unit, NetworkFailure>> SendAsync(SecureEnvelope envelope);
}
