using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public static class GrpcErrorHandler
{
    public static NetworkFailure ClassifyRpcException(RpcException rpcEx) =>
        GrpcErrorClassifier.IsBusinessError(rpcEx) || GrpcErrorClassifier.IsAuthenticationError(rpcEx)
            ? NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}")
            : GrpcErrorClassifier.IsCancelled(rpcEx)
                ? throw rpcEx
                : GrpcErrorClassifier.IsProtocolStateMismatch(rpcEx)
                    ? NetworkFailure.ProtocolStateMismatch(rpcEx.Status.Detail ?? "Protocol state mismatch")
                    : GrpcErrorClassifier.IsServerShutdown(rpcEx)
                        ? NetworkFailure.DataCenterShutdown(rpcEx.Status.Detail ?? "Server unavailable")
                        : GrpcErrorClassifier.RequiresHandshakeRecovery(rpcEx)
                            ? NetworkFailure.DataCenterNotResponding(
                                rpcEx.Status.Detail ?? "Connection recovery needed")
                            : GrpcErrorClassifier.IsTransientInfrastructure(rpcEx)
                                ? NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Temporary failure")
                                : NetworkFailure.DataCenterNotResponding(rpcEx.Message);

    public static async Task<NetworkFailure> ClassifyRpcExceptionWithEventsAsync(
        RpcException rpcEx,
        INetworkEventService networkEvents)
    {
        if (GrpcErrorClassifier.IsBusinessError(rpcEx) || GrpcErrorClassifier.IsAuthenticationError(rpcEx))
            return NetworkFailure.InvalidRequestType($"{rpcEx.StatusCode}: {rpcEx.Status.Detail}");

        if (GrpcErrorClassifier.IsCancelled(rpcEx))
            throw rpcEx;

        if (GrpcErrorClassifier.IsProtocolStateMismatch(rpcEx))
            return NetworkFailure.ProtocolStateMismatch(rpcEx.Status.Detail ?? "Protocol state mismatch");

        if (GrpcErrorClassifier.IsServerShutdown(rpcEx))
        {
            return NetworkFailure.DataCenterShutdown(rpcEx.Status.Detail ?? "Server unavailable");
        }

        if (GrpcErrorClassifier.RequiresHandshakeRecovery(rpcEx))
        {
            return NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Connection recovery needed");
        }

        if (GrpcErrorClassifier.IsTransientInfrastructure(rpcEx))
            return NetworkFailure.DataCenterNotResponding(rpcEx.Status.Detail ?? "Temporary failure");

        return NetworkFailure.DataCenterNotResponding(rpcEx.Message);
    }
}
