using System;
using System.Collections.Generic;
using Ecliptix.Protobuf.CipherPayload;

namespace Ecliptix.Core.Network;

public class ServiceRequest
{
    public uint ReqId { get; }

    public ServiceFlowType ActionType { get; }

    public RcpServiceAction RcpServiceMethod { get; }

    public CipherPayload Payload { get; }

    public List<CipherPayload> EncryptedChunks { get; }

    private ServiceRequest(uint reqId, ServiceFlowType actionType, RcpServiceAction rcpServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        ReqId = reqId;
        ActionType = actionType;
        RcpServiceMethod = rcpServiceMethod;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        EncryptedChunks = encryptedChunks;
    }

    public static ServiceRequest New(ServiceFlowType actionType, RcpServiceAction rcpServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        uint reqId = Utilities.GenerateRandomUInt32InRange(10, uint.MaxValue);
        return new ServiceRequest(reqId, actionType, rcpServiceMethod, payload, encryptedChunks);
    }

    public static ServiceRequest NewWithId(uint reqId, ServiceFlowType actionType, RcpServiceAction rcpServiceMethod,
        CipherPayload payload, List<CipherPayload> encryptedChunks)
    {
        return new ServiceRequest(reqId, actionType, rcpServiceMethod, payload, encryptedChunks);
    }
}