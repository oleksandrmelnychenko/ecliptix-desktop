using System;
using System.Threading.Tasks;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public class PubKeyExchangeActionInvokable
{
    public uint ReqId { get; }

    public ServiceFlowType JobType { get; }

    public RcpServiceAction Method { get; }

    public PubKeyExchange PubKeyExchange { get; }

    public Func<PubKeyExchange, Task>? OnComplete { get; }

    private PubKeyExchangeActionInvokable(
        uint reqId,
        ServiceFlowType jobType,
        RcpServiceAction method,
        PubKeyExchange pubKeyExchange,
        Func<PubKeyExchange, Task>? onComplete)
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
        Func<PubKeyExchange, Task>? callback = null)
    {
        uint reqId = ServiceUtilities.GenerateRandomUInt32();
        return new PubKeyExchangeActionInvokable(reqId, jobType, method, pubKeyExchange, callback);
    }

    public static PubKeyExchangeActionInvokable WithReqId(
        uint reqId,
        ServiceFlowType jobType,
        RcpServiceAction method,
        PubKeyExchange pubKeyExchange,
        Func<PubKeyExchange, Task>? callback = null)
    {
        return new PubKeyExchangeActionInvokable(reqId, jobType, method, pubKeyExchange, callback);
    }

    public ServiceRequest ToServiceRequest()
    {
        return ServiceRequest.NewWithId(ReqId,JobType, Method, new CipherPayload(), []);
    }
}