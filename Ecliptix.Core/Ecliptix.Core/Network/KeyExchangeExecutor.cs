using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.PubKeyExchange;

namespace Ecliptix.Core.Network;

public sealed class KeyExchangeExecutor(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient)
{
    public async Task<Result<PubKeyExchange, ShieldFailure>> BeginDataCenterPublicKeyExchange(PubKeyExchange request)
    {
        return await Result<PubKeyExchange, ShieldFailure>.TryAsync(async () =>
        {
            PubKeyExchange? response =
                await appDeviceServiceActionsClient.EstablishAppDeviceEphemeralConnectAsync(request);
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }
}