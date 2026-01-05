using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface IUnaryRpcServices
{
    Task<Result<RpcFlow, NetworkFailure>> InvokeRequestAsync(
        ServiceRequest request,
        IConnectivityService connectivityService,
        CancellationToken token);
}
