using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public class NetworkServiceManager
{
    private readonly ConcurrentDictionary<RcpServiceAction, Task> _activeStreamHandles;
    private readonly KeyExchangeExecutor _keyExchangeExecutor;
    private readonly ReceiveStreamExecutor _receiveStreamExecutor;
    private readonly SingleCallExecutor _singleCallExecutor;

    public NetworkServiceManager(
        SingleCallExecutor singleCallExecutor, ReceiveStreamExecutor receiveStreamExecutor,
        KeyExchangeExecutor keyExchangeExecutor)
    {
        _singleCallExecutor = singleCallExecutor;
        _receiveStreamExecutor = receiveStreamExecutor;
        _keyExchangeExecutor = keyExchangeExecutor;
        _activeStreamHandles = new ConcurrentDictionary<RcpServiceAction, Task>();
    }

    public async Task BeginDataCenterPublicKeyExchange(PubKeyExchangeActionInvokable action)
    {
        Result<PubKeyExchange, ShieldFailure> beginPubkeyExchangeResult =
            await _keyExchangeExecutor.BeginDataCenterPublicKeyExchange(action.PubKeyExchange);

        if (beginPubkeyExchangeResult.IsOk)
            if (action.OnComplete != null)
                action.OnComplete(beginPubkeyExchangeResult.Unwrap());
    }

    public async Task<Result<RpcFlow, ShieldFailure>> InvokeServiceRequestAsync(ServiceRequest request,
        CancellationToken token)
    {
        RcpServiceAction action = request.RcpServiceMethod;

        Result<RpcFlow, ShieldFailure> result = default;
        switch (request.ActionType)
        {
            case ServiceFlowType.Single:
                result = await _singleCallExecutor.InvokeRequestAsync(request, token);
                break;
            case ServiceFlowType.ReceiveStream:
                result = await _receiveStreamExecutor.ProcessRequestAsync(request, token);
                break;
        }

        Console.WriteLine($"Action {action} executed successfully for req_id: {request.ReqId}");
        return result;
    }
}