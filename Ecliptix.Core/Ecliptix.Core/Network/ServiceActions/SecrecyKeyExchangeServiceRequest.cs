using System;
using Ecliptix.Core.Protocol.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Network.ServiceActions;

public class SecrecyKeyExchangeServiceRequest<TRequest, TResponse> where TRequest : IMessage<TRequest>
{
    public uint ReqId { get; }

    public ServiceFlowType JobType { get; }

    public RcpServiceType Method { get; }

    public TRequest PubKeyExchange { get; }

    public static SecrecyKeyExchangeServiceRequest<TRequest, TResponse> New(
        ServiceFlowType jobType,
        RcpServiceType method,
        TRequest pubKeyExchange)
    {
        uint reqId = Utilities.GenerateRandomUInt32();
        return new SecrecyKeyExchangeServiceRequest<TRequest, TResponse>(reqId, jobType, method, pubKeyExchange);
    }

    private SecrecyKeyExchangeServiceRequest(
        uint reqId,
        ServiceFlowType jobType,
        RcpServiceType method,
        TRequest pubKeyExchange)
    {
        ReqId = reqId;
        JobType = jobType;
        Method = method;
        PubKeyExchange = pubKeyExchange ?? throw new ArgumentNullException(nameof(pubKeyExchange));
    }
}