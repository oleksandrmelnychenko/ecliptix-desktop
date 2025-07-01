using System.Collections.Generic;
using Ecliptix.Protobuf.CipherPayload;

namespace Ecliptix.Core.Network.ServiceActions;

public class ServiceRequest
{
    private ServiceRequest(uint reqId, ServiceFlowType actionType, RcpServiceType rcpServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        ReqId = reqId;
        ActionType = actionType;
        RcpServiceMethod = rcpServiceMethod;
        Payload = payload;
        EncryptedChunks = encryptedChunks;
    }

    public uint ReqId { get; }

    public ServiceFlowType ActionType { get; }

    public RcpServiceType RcpServiceMethod { get; }

    public CipherPayload Payload { get; }

    public List<CipherPayload> EncryptedChunks { get; }

    public static ServiceRequest New(ServiceFlowType actionType, RcpServiceType rcpServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        uint reqId = Utilities.GenerateRandomUInt32InRange(10, uint.MaxValue);
        return new ServiceRequest(reqId, actionType, rcpServiceMethod, payload, encryptedChunks);
    }
}