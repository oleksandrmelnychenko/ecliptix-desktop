using Ecliptix.Network.Services.Network.Rpc;
using Grpc.Core;

namespace Ecliptix.Network.Services.Network;

public interface IGrpcCallOptionsFactory
{
    CallOptions Create(
        RpcServiceType serviceType,
        RpcRequestContext? requestContext,
        CancellationToken cancellationToken,
        Metadata? additionalHeaders = null);
}
