using Ecliptix.Network.Services.Network.Rpc;

namespace Ecliptix.Network.Services.Network;

public interface IGrpcDeadlineProvider
{
    DateTime GetDeadlineUtc(RpcServiceType serviceType, RpcRequestContext? requestContext);
}
