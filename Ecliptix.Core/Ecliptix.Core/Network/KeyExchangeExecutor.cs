using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public sealed class KeyExchangeExecutor(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient)
{
    public async Task<Result<PubKeyExchange, EcliptixProtocolFailure>> BeginDataCenterPublicKeyExchange(PubKeyExchange request)
    {
        return await Result<PubKeyExchange, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            var response =
                await appDeviceServiceActionsClient.EstablishAppDeviceEphemeralConnectAsync(request);
            return response;
        }, err => 
            EcliptixProtocolFailure.Generic(err.Message, err.InnerException));
    }
}