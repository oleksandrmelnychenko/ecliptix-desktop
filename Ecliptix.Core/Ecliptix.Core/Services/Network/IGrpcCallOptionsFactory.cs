using System.Threading;
using Ecliptix.Core.Services.Network.Rpc;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network;

public interface IGrpcCallOptionsFactory
{
    CallOptions Create(
        RpcServiceType serviceType,
        RpcRequestContext? requestContext,
        CancellationToken cancellationToken,
        Metadata? additionalHeaders = null);
}
