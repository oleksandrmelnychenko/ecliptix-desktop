using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IReceiveStreamRpcServices
{
    Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request, CancellationToken token);
}