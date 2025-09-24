using System.Collections.Generic;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Network.Rpc;

public class ServiceRequest
{
    private ServiceRequest(uint reqId, ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        SecureEnvelope payload, List<SecureEnvelope> encryptedChunks)
    {
        ReqId = reqId;
        ActionType = actionType;
        RpcServiceMethod = rpcServiceMethod;
        Payload = payload;
        EncryptedChunks = encryptedChunks;
    }

    public uint ReqId { get; }

    public ServiceFlowType ActionType { get; }

    public RpcServiceType RpcServiceMethod { get; }

    public SecureEnvelope Payload { get; }

    public List<SecureEnvelope> EncryptedChunks { get; }

    public static ServiceRequest New(ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        SecureEnvelope payload, List<SecureEnvelope> encryptedChunks)
    {
        uint reqId = Helpers.GenerateRandomUInt32InRange(UtilityConstants.NetworkConstants.MinRequestId, uint.MaxValue);
        return new ServiceRequest(reqId, actionType, rpcServiceMethod, payload, encryptedChunks);
    }

    public static ServiceRequest New(uint reqId, ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        SecureEnvelope payload, List<SecureEnvelope> encryptedChunks)
    {
        return new ServiceRequest(reqId, actionType, rpcServiceMethod, payload, encryptedChunks);
    }
}