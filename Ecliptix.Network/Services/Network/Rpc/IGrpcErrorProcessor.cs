using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Network.Services.Network.Rpc;

public interface IGrpcErrorProcessor
{
    NetworkFailure Process(RpcException rpcException);
    Task<NetworkFailure> ProcessAsync(RpcException rpcException);
}
