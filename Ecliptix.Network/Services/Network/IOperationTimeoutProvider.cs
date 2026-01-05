using Ecliptix.Network.Services.Network.Rpc;

namespace Ecliptix.Network.Services.Network;

public interface IOperationTimeoutProvider
{
    TimeSpan GetTimeout(RpcServiceType serviceType, RpcRequestContext? requestContext = null);
}
