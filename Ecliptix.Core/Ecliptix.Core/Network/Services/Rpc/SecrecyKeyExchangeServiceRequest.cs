using System;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Network.Services.Rpc;

public class SecrecyKeyExchangeServiceRequest<TRequest, TResponse> where TRequest : IMessage<TRequest>
{
    public uint ReqId { get; }

    public ServiceFlowType JobType { get; }

    public RpcServiceType Method { get; }

    public TRequest PubKeyExchange { get; }

    public static SecrecyKeyExchangeServiceRequest<TRequest, TResponse> New(
        ServiceFlowType jobType,
        RpcServiceType method,
        TRequest pubKeyExchange)
    {
        uint reqId = Helpers.GenerateRandomUInt32();
        return new SecrecyKeyExchangeServiceRequest<TRequest, TResponse>(reqId, jobType, method, pubKeyExchange);
    }

    private SecrecyKeyExchangeServiceRequest(
        uint reqId,
        ServiceFlowType jobType,
        RpcServiceType method,
        TRequest pubKeyExchange)
    {
        ReqId = reqId;
        JobType = jobType;
        Method = method;
        PubKeyExchange = pubKeyExchange ?? throw new ArgumentNullException(nameof(pubKeyExchange));
    }
}