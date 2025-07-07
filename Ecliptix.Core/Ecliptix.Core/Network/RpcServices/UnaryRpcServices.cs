using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Core.ResilienceStrategy;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Grpc.Core;
using Polly.Retry;

namespace Ecliptix.Core.Network.RpcServices;

public sealed class UnaryRpcServices
{
    private readonly Dictionary<RcpServiceType, GrpcMethodDelegate> _serviceMethods;

    private delegate Task<Result<CipherPayload, EcliptixProtocolFailure>> GrpcMethodDelegate(CipherPayload payload,
        CancellationToken token);

    public UnaryRpcServices(
        MembershipServices.MembershipServicesClient membershipServicesClient,
        AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient,
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient)
    {
        _serviceMethods = new Dictionary<RcpServiceType, GrpcMethodDelegate>
        {
            [RcpServiceType.RegisterAppDevice] = RegisterDeviceAsync,
            [RcpServiceType.ValidatePhoneNumber] = ValidatePhoneNumberAsync,
            [RcpServiceType.OpaqueRegistrationInit] = OpaqueRegistrationRecordRequestAsync,
            [RcpServiceType.VerifyOtp] = VerifyCodeAsync,
            [RcpServiceType.OpaqueRegistrationComplete] = OpaqueRegistrationCompleteRequestAsync,
            [RcpServiceType.OpaqueSignInInitRequest] = OpaqueSignInInitRequestAsync,
            [RcpServiceType.OpaqueSignInCompleteRequest] = OpaqueSignInCompleteRequestAsync
        };
        return;

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> RegisterDeviceAsync(CipherPayload payload,
            CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                appDeviceServiceActionsClient.RegisterDeviceAppIfNotExistAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> ValidatePhoneNumberAsync(CipherPayload payload,
            CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                authenticationServicesClient.ValidatePhoneNumberAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> OpaqueRegistrationRecordRequestAsync(
            CipherPayload payload, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                membershipServicesClient.OpaqueRegistrationInitRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> VerifyCodeAsync(CipherPayload payload,
            CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                authenticationServicesClient.VerifyOtpAsync(payload, new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> OpaqueRegistrationCompleteRequestAsync(
            CipherPayload payload, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                membershipServicesClient.OpaqueRegistrationCompleteRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> OpaqueSignInInitRequestAsync(
            CipherPayload payload, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                membershipServicesClient.OpaqueSignInInitRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, EcliptixProtocolFailure>> OpaqueSignInCompleteRequestAsync(
            CipherPayload payload, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(() =>
                membershipServicesClient.OpaqueSignInCompleteRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }
    }

    public async Task<Result<RpcFlow, EcliptixProtocolFailure>> InvokeRequestAsync(ServiceRequest request, CancellationToken token)
    {
        if (_serviceMethods.TryGetValue(request.RcpServiceMethod, out GrpcMethodDelegate? method))
        {
            Result<CipherPayload, EcliptixProtocolFailure> result = await method(request.Payload, token);
            return Result<RpcFlow, EcliptixProtocolFailure>.Ok(new RpcFlow.SingleCall(Task.FromResult(result)));
        }

        return Result<RpcFlow, EcliptixProtocolFailure>.Err(
            EcliptixProtocolFailure.Generic("Unknown service type"));
    }

    private static async Task<Result<CipherPayload, EcliptixProtocolFailure>> ExecuteGrpcCallAsync(
        Func<AsyncUnaryCall<CipherPayload>> grpcCallFactory)
    {
        AsyncRetryPolicy<CipherPayload> policy = RpcResiliencePolicies.GetSecrecyChannelRetryPolicy<CipherPayload>();
        return await Result<CipherPayload, EcliptixProtocolFailure>.TryAsync(
            async () => await policy.ExecuteAsync(async () =>
            {
                AsyncUnaryCall<CipherPayload> call = grpcCallFactory();
                return await call.ResponseAsync;
            }),
            err => EcliptixProtocolFailure.Generic(err.Message, err.InnerException));
    }
}