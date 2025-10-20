using System;
using Ecliptix.Core.Services.Network.Rpc;

namespace Ecliptix.Core.Services.Network;

public interface IOperationTimeoutProvider
{
    TimeSpan GetTimeout(RpcServiceType serviceType, RpcRequestContext? requestContext = null);
}
