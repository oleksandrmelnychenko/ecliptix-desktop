using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.ResilienceStrategy;
using Ecliptix.Core.Network.ServiceActions;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Polly.Retry;

namespace Ecliptix.Core.Network.RpcServices;

public sealed class UnaryRpcServices
{
    private readonly Dictionary<RcpServiceType, GrpcMethodDelegate> _serviceMethods;

    private delegate Task<Result<CipherPayload, NetworkFailure>> GrpcMethodDelegate(CipherPayload payload,
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
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

        async Task<Result<CipherPayload, NetworkFailure>> RegisterDeviceAsync(CipherPayload payload,
            INetworkEvents networkEvents,
            ISystemEvents systemEvents,
            CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                appDeviceServiceActionsClient.RegisterDeviceAppIfNotExistAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, NetworkFailure>> ValidatePhoneNumberAsync(CipherPayload payload,
            INetworkEvents networkEvents,
            ISystemEvents systemEvents,
            CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                authenticationServicesClient.ValidatePhoneNumberAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueRegistrationRecordRequestAsync(
            CipherPayload payload, INetworkEvents networkEvents, ISystemEvents systemEvents, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                membershipServicesClient.OpaqueRegistrationInitRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, NetworkFailure>> VerifyCodeAsync(CipherPayload payload,
            INetworkEvents networkEvents,
            ISystemEvents systemEvents,
            CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                authenticationServicesClient.VerifyOtpAsync(payload, new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueRegistrationCompleteRequestAsync(
            CipherPayload payload, INetworkEvents networkEvents, ISystemEvents systemEvents, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                membershipServicesClient.OpaqueRegistrationCompleteRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueSignInInitRequestAsync(
            CipherPayload payload, INetworkEvents networkEvents, ISystemEvents systemEvents, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                membershipServicesClient.OpaqueSignInInitRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueSignInCompleteRequestAsync(
            CipherPayload payload, INetworkEvents networkEvents, ISystemEvents systemEvents, CancellationToken token)
        {
            return await ExecuteGrpcCallAsync(networkEvents, systemEvents, () =>
                membershipServicesClient.OpaqueSignInCompleteRequestAsync(payload,
                    new CallOptions(cancellationToken: token)));
        }
    }

    public async Task<Result<RpcFlow, NetworkFailure>> InvokeRequestAsync(ServiceRequest request,
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        CancellationToken token)
    {
        if (_serviceMethods.TryGetValue(request.RcpServiceMethod, out GrpcMethodDelegate? method))
        {
            Result<CipherPayload, NetworkFailure> result = await method(request.Payload, networkEvents, systemEvents,
                token);
            return Result<RpcFlow, NetworkFailure>.Ok(new RpcFlow.SingleCall(Task.FromResult(result)));
        }

        return Result<RpcFlow, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Unknown service type"));
    }

    private static async Task<Result<CipherPayload, NetworkFailure>> ExecuteGrpcCallAsync(
        INetworkEvents networkEvents,
        ISystemEvents systemEvents,
        Func<AsyncUnaryCall<CipherPayload>> grpcCallFactory)
    {
        try
        {
            AsyncRetryPolicy<CipherPayload> policy =
                RpcResiliencePolicies.CreateSecrecyChannelRetryPolicy<CipherPayload>(networkEvents);

            CipherPayload? response = await policy.ExecuteAsync(async () =>
            {
                AsyncUnaryCall<CipherPayload> call = grpcCallFactory();
                return await call.ResponseAsync;
            });

            networkEvents.InitiateChangeState(NetworkStatusChangedEvent.New(NetworkStatus.DataCenterConnected));

            return Result<CipherPayload, NetworkFailure>.Ok(response);
        }
        catch (Exception exc)
        {
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.DataCenterShutdown));

            return Result<CipherPayload, NetworkFailure>.Err(
                NetworkFailure.DataCenterShutdown(exc.Message));
        }
    }
}