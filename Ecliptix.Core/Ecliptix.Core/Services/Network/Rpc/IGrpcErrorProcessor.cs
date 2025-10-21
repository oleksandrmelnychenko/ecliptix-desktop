using System.Threading.Tasks;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public interface IGrpcErrorProcessor
{
    NetworkFailure Process(RpcException rpcException);
    Task<NetworkFailure> ProcessAsync(RpcException rpcException);
}
