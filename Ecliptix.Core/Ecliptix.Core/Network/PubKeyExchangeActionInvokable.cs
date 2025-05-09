using System;
using System.Threading.Tasks;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public class PubKeyExchangeActionInvokable
{
    public uint ReqId { get; }

    public ServiceFlowType JobType { get; }

    public RcpServiceAction Method { get; }

    public PubKeyExchange PubKeyExchange { get; }

    public Action<PubKeyExchange>? OnComplete { get; }

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

    public static PubKeyExchangeActionInvokable New(
        ServiceFlowType jobType,
        RcpServiceAction method,
        PubKeyExchange pubKeyExchange,
        Action<PubKeyExchange>? callback = null)
    {
        uint reqId = ServiceUtilities.GenerateRandomUInt32();
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