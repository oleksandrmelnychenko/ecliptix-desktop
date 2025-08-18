using System.Collections.Generic;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Network.Rpc;

public class ServiceRequest
{
    private ServiceRequest(uint reqId, ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
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

    public CipherPayload Payload { get; }

    public List<CipherPayload> EncryptedChunks { get; }

    public static ServiceRequest New(ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        uint reqId = Helpers.GenerateRandomUInt32InRange(10, uint.MaxValue);
        return new ServiceRequest(reqId, actionType, rpcServiceMethod, payload, encryptedChunks);
    }

    public static ServiceRequest New(uint reqId, ServiceFlowType actionType, RpcServiceType rpcServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        return new ServiceRequest(reqId, actionType, rpcServiceMethod, payload, encryptedChunks);
    }
}