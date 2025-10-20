using System;
using Ecliptix.Core.Services.Network.Rpc;

namespace Ecliptix.Core.Services.Network;

public interface IGrpcDeadlineProvider
{
    DateTime GetDeadlineUtc(RpcServiceType serviceType, RpcRequestContext? requestContext);
}
