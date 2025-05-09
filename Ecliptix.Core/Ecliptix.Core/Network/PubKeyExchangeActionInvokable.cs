using System;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public class PubKeyExchangeActionInvokable
{
    private PubKeyExchangeActionInvokable(
        uint reqId,
        ServiceFlowType jobType,
        RcpServiceAction method,
        PubKeyExchange pubKeyExchange,
        Action<PubKeyExchange>? onComplete)
    {
        ReqId = reqId;
        JobType = jobType;
        Method = method;
        PubKeyExchange = pubKeyExchange ?? throw new ArgumentNullException(nameof(pubKeyExchange));
        OnComplete = onComplete;
    }

    public uint ReqId { get; }

    public ServiceFlowType JobType { get; }

    public RcpServiceAction Method { get; }

    public PubKeyExchange PubKeyExchange { get; }

    public Action<PubKeyExchange>? OnComplete { get; }

    public static PubKeyExchangeActionInvokable New(
        ServiceFlowType jobType,
        RcpServiceAction method,
        PubKeyExchange pubKeyExchange,
        Action<PubKeyExchange>? callback = null)
    {
        uint reqId = Utilities.GenerateRandomUInt32();
        return new PubKeyExchangeActionInvokable(reqId, jobType, method, pubKeyExchange, callback);
    }

    public static PubKeyExchangeActionInvokable WithReqId(
        uint reqId,
        ServiceFlowType jobType,
        RcpServiceAction method,
        PubKeyExchange pubKeyExchange,
        Action<PubKeyExchange>? callback = null)
    {
        return new PubKeyExchangeActionInvokable(reqId, jobType, method, pubKeyExchange, callback);
    }
}