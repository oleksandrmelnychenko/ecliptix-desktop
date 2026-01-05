using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Services.Abstractions.Network;

public interface IReceiveStreamRpcServices
{
    Task<Result<RpcFlow, NetworkFailure>> ProcessRequest(ServiceRequest request, CancellationToken token);
}
