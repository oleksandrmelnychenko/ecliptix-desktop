using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface IReceiveStreamRpcServices
{
    Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request, CancellationToken token);
}
