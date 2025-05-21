using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public sealed class SingleCallExecutor(
    MembershipServices.MembershipServicesClient membershipServicesClient,
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient,
    AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
{
    public Task<Result<RpcFlow, ShieldFailure>> InvokeRequestAsync(ServiceRequest request, CancellationToken token)
    {
        switch (request.RcpServiceMethod)
        {
            case RcpServiceAction.RegisterAppDevice:
                Task<Result<CipherPayload, ShieldFailure>> result = RegisterDeviceAsync(request.Payload, token);
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(result)));
            case RcpServiceAction.ValidatePhoneNumber:
                Task<Result<CipherPayload, ShieldFailure>> validatePhoneNumberResult =
                    ValidatePhoneNumberAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(validatePhoneNumberResult)));
            case RcpServiceAction.SignIn:
                Task<Result<CipherPayload, ShieldFailure>> signInResult =
                    SignInAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(signInResult)));
            case RcpServiceAction.CreateMembership:
                Task<Result<CipherPayload, ShieldFailure>> createMembershipResult =
                    CreateMembershipAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(createMembershipResult)));
            case RcpServiceAction.InitiateResendVerification:
                Task<Result<CipherPayload, ShieldFailure>> initiateResendVerificationResult =
                    InitiateResendVerificationAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(initiateResendVerificationResult)));
            case RcpServiceAction.VerifyOtp:
                Task<Result<CipherPayload, ShieldFailure>> verifyWithCodeResult =
                    VerifyCodeAsync(request.Payload, token);
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Ok(new RpcFlow.SingleCall(verifyWithCodeResult)));
            default:
                return Task.FromResult(Result<RpcFlow, ShieldFailure>.Err(
                    ShieldFailure.Generic()
                ));
        }
    }

    private async Task<Result<CipherPayload, ShieldFailure>> CreateMembershipAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await membershipServicesClient.CreateMembershipAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, ShieldFailure>> SignInAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await membershipServicesClient.SignInMembershipAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, ShieldFailure>> InitiateResendVerificationAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await authenticationServicesClient.InitiateResendVerificationAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, ShieldFailure>> ValidatePhoneNumberAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await authenticationServicesClient.ValidatePhoneNumberAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, ShieldFailure>> VerifyCodeAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, ShieldFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await authenticationServicesClient.VerifyOtpAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => ShieldFailure.Generic(err.Message, err.InnerException));
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