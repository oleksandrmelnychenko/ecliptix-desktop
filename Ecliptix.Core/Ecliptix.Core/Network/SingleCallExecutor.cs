using System.Threading.Tasks;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;

namespace Ecliptix.Core.Network;

public sealed class SingleCallExecutor(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient,
    VerificationServiceActions.VerificationServiceActionsClient verificationServiceActionsClient)
{
    public Task<Result<RpcFlow, ShieldFailure>> InvokeRequestAsync(ServiceRequest request)
    {
        switch (request.RcpServiceMethod)
        {
            case RcpServiceAction.RegisterAppDeviceIfNotExist:
                Task<Result<CipherPayload, ShieldFailure>> result = RegisterDeviceAsync(request.Payload);
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(result)));
            case RcpServiceAction.SendVerificationCode:
            case RcpServiceAction.VerifyCode:
            default:
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Err(
                    ShieldFailure.Generic()
                ));
        }
    }

    private async Task<Result<CipherPayload, ShieldFailure>> RegisterDeviceAsync(CipherPayload payload)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response = await appDeviceServiceActionsClient.RegisterDeviceAppIfNotExistAsync(payload);
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }
}