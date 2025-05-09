using System;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public sealed class NetworkController
{
    private readonly NetworkServiceManager _networkServiceManager;
    private readonly EcliptixProtocolSystem _ecliptixProtocolSystem;

    public NetworkController(NetworkServiceManager networkServiceManager)
    {
        _networkServiceManager = networkServiceManager;

        EcliptixSystemIdentityKeys ecliptixSystemIdentityKeys =
            EcliptixSystemIdentityKeys.Create(10).Unwrap();
        _ecliptixProtocolSystem = new EcliptixProtocolSystem(ecliptixSystemIdentityKeys);
    }

    public async Task<Result<Unit, ShieldFailure>> ExecuteServiceActionAsync(
        RcpServiceAction rcpServiceAction,
        byte[] plainBuffer,
        PubKeyExchangeType pubKeyExchangeOfType,
        ServiceFlowType jobType,
        Func<byte[], Task<Result<Unit, ShieldFailure>>> callback)
    {
        throw new NotImplementedException();
        
    }
    
    public async Task<Result<Unit, ShieldFailure>> InitiateKeyExchangeAsync(
        PubKeyExchangeType pubKeyExchangeOfType,
        Func<Task<Result<Unit, ShieldFailure>>>? registerCallback = null)
    {
        throw new NotImplementedException();
    }
}