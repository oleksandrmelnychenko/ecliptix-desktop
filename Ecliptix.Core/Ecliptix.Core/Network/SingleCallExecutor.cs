using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public sealed class SingleCallExecutor(
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient,
    VerificationServiceActions.VerificationServiceActionsClient verificationServiceActionsClient)
{
    public Task<Result<RpcFlow, ShieldFailure>> InvokeRequestAsync(ServiceRequest request, CancellationToken token)
    {
        switch (request.RcpServiceMethod)
        {
            case RcpServiceAction.RegisterAppDeviceIfNotExist:
                Task<Result<CipherPayload, ShieldFailure>> result = RegisterDeviceAsync(request.Payload, token);
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(result)));
            case RcpServiceAction.SendVerificationCode:
            case RcpServiceAction.VerifyCode:
            default:
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Err(
                    ShieldFailure.Generic()
                ));
        }
    }

    private async Task<Result<CipherPayload, ShieldFailure>> RegisterDeviceAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await appDeviceServiceActionsClient.RegisterDeviceAppIfNotExistAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }
}