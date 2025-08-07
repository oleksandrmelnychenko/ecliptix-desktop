using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Services;

public interface IUnaryRpcServices
{
    Task<Result<RpcFlow, NetworkFailure>> InvokeRequestAsync(
        ServiceRequest request,
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        CancellationToken token);
}