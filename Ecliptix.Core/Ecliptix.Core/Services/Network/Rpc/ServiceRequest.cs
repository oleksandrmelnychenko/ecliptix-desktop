using System.Collections.Generic;
using Ecliptix.Protobuf.Common;

namespace Ecliptix.Core.Services.Network.Rpc;

public class ServiceRequest
{
    private ServiceRequest(
        uint reqId,
        ServiceFlowType actionType,
        RpcServiceType rpcServiceMethod,
        SecureEnvelope payload,
        List<SecureEnvelope> encryptedChunks,
        RpcRequestContext? requestContext)
    {
        ReqId = reqId;
        ActionType = actionType;
        RpcServiceMethod = rpcServiceMethod;
        Payload = payload;
        EncryptedChunks = encryptedChunks;
        RequestContext = requestContext;
    }

    public uint ReqId { get; }

    public ServiceFlowType ActionType { get; }

    public RpcServiceType RpcServiceMethod { get; }

    public SecureEnvelope Payload { get; }

    public List<SecureEnvelope> EncryptedChunks { get; }

    public RpcRequestContext? RequestContext { get; }

    public static ServiceRequest New(uint reqId, ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        SecureEnvelope payload, List<SecureEnvelope> encryptedChunks, RpcRequestContext? requestContext = null) =>
        new(reqId, actionType, rpcServiceMethod, payload, encryptedChunks, requestContext);
}
