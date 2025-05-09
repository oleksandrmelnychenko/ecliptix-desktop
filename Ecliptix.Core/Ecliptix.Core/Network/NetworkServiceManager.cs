using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;
using ReactiveUI;

namespace Ecliptix.Core.Network;

public class NetworkServiceManager
{
    private readonly SingleCallExecutor _singleCallExecutor;
    private readonly ReceiveStreamExecutor _receiveStreamExecutor;
    private readonly KeyExchangeExecutor _keyExchangeExecutor;
    private readonly ConcurrentDictionary<RcpServiceAction, Task> _activeStreamHandles;

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
        {
            if (action.OnComplete != null)
            {
                action.OnComplete(beginPubkeyExchangeResult.Unwrap());
            }
        }
    }

    public async Task<Result<RpcFlow, ShieldFailure>> InvokeServiceRequestAsync(ServiceRequest request)
    {
        RcpServiceAction action = request.RcpServiceMethod;

        Result<RpcFlow, ShieldFailure> result = default;
        switch (request.ActionType)
        {
            case ServiceFlowType.Single:
                result = await _singleCallExecutor.InvokeRequestAsync(request);
                break;
            case ServiceFlowType.ReceiveStream:
                result = await _receiveStreamExecutor.ProcessRequestAsync(request);
                break;
        }

        Console.WriteLine($"Action {action} executed successfully for req_id: {request.ReqId}");
        return result;
    }
}