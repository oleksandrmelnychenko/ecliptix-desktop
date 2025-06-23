using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Grpc.Core;

namespace Ecliptix.Core.Network;

public sealed class SingleCallExecutor(
    MembershipServices.MembershipServicesClient membershipServicesClient,
    AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient,
    AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
{
    public Task<Result<RpcFlow, EcliptixProtocolFailure>> InvokeRequestAsync(ServiceRequest request, CancellationToken token)
    {
        switch (request.RcpServiceMethod)
        {
            case RcpServiceAction.RegisterAppDevice:
                Task<Result<CipherPayload, EcliptixProtocolFailure>> result = RegisterDeviceAsync(request.Payload, token);
                return Task.FromResult(Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.SingleCall(result)));
            case RcpServiceAction.ValidatePhoneNumber:
                Task<Result<CipherPayload, EcliptixProtocolFailure>> validatePhoneNumberResult =
                    ValidatePhoneNumberAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.SingleCall(validatePhoneNumberResult)));
            case RcpServiceAction.SignIn:
                Task<Result<CipherPayload, EcliptixProtocolFailure>> signInResult =
                    SignInAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.SingleCall(signInResult)));
            case RcpServiceAction.OpaqueRegistrationRecord:
                Task<Result<CipherPayload, EcliptixProtocolFailure>> createMembershipResult =
                    OpaqueRegistrationRecordRequestAsync(request.Payload, token);
                return Task.FromResult(
                    Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.SingleCall(createMembershipResult)));
            case RcpServiceAction.VerifyOtp:
                Task<Result<CipherPayload, EcliptixProtocolFailure>> verifyWithCodeResult =
                    VerifyCodeAsync(request.Payload, token);
                return Task.FromResult(Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.SingleCall(verifyWithCodeResult)));
            default:
                return Task.FromResult(Result<RpcFlow, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("")
                ));
        }
    }

    private async Task<Result<CipherPayload, EcliptixProtocolFailure>> OpaqueRegistrationRecordRequestAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await membershipServicesClient.OpaqueRegistrationRecordRequestAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => EcliptixProtocolFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, EcliptixProtocolFailure>> SignInAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, EcliptixProtocolFailure>.TryAsync(async () =>
            {
                CipherPayload? response =
                    await membershipServicesClient.SignInMembershipAsync(payload,
                        new CallOptions(cancellationToken: token)
                    );
                return response;
            },
            err => err);
    }

    private async Task<Result<CipherPayload, EcliptixProtocolFailure>> ValidatePhoneNumberAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await authenticationServicesClient.ValidatePhoneNumberAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => EcliptixProtocolFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, EcliptixProtocolFailure>> VerifyCodeAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await authenticationServicesClient.VerifyOtpAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => EcliptixProtocolFailure.Generic(err.Message, err.InnerException));
    }

    private async Task<Result<CipherPayload, EcliptixProtocolFailure>> RegisterDeviceAsync(CipherPayload payload,
        CancellationToken token)
    {
        return await Result<CipherPayload, EcliptixProtocolFailure>.TryAsync(async () =>
        {
            CipherPayload? response =
                await appDeviceServiceActionsClient.RegisterDeviceAppIfNotExistAsync(payload,
                    new CallOptions(cancellationToken: token)
                );
            return response;
        }, err => EcliptixProtocolFailure.Generic(err.Message, err.InnerException));
    }
}