using System;
using Ecliptix.Core.Protocol.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Network.ServiceActions;

public class SecrecyKeyExchangeServiceRequest<TRequest, TResponse> where TRequest : IMessage<TRequest>
{
    private SecrecyKeyExchangeServiceRequest(
        uint reqId,
        ServiceFlowType jobType,
        RcpServiceType method,
        TRequest pubKeyExchange,
        Action<TResponse> onComplete,
        Action<EcliptixProtocolFailure> onFailure)
    {
        ReqId = reqId;
        JobType = jobType;
        Method = method;
        PubKeyExchange = pubKeyExchange ?? throw new ArgumentNullException(nameof(pubKeyExchange));
        OnComplete = onComplete;
        OnFailure = onFailure;
    }

    public uint ReqId { get; }

    public ServiceFlowType JobType { get; }

    public RcpServiceType Method { get; }

    public TRequest PubKeyExchange { get; }

    public Action<TResponse> OnComplete { get; }

    public Action<EcliptixProtocolFailure> OnFailure { get; }

    public static SecrecyKeyExchangeServiceRequest<TRequest, TResponse> New(
        ServiceFlowType jobType,
        RcpServiceType method,
        TRequest pubKeyExchange,
        Action<TResponse> onComplete,
        Action<EcliptixProtocolFailure> onFailure)
    {
        uint reqId = Utilities.GenerateRandomUInt32();
        return new SecrecyKeyExchangeServiceRequest<TRequest, TResponse>(reqId, jobType, method, pubKeyExchange, onComplete,
            onFailure);
    }
}