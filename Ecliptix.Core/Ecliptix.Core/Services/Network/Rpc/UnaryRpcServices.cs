using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.AppDeviceServices;
using Ecliptix.Protobuf.CipherPayload;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Serilog;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class UnaryRpcServices : IUnaryRpcServices
{
    private readonly Dictionary<RpcServiceType, GrpcMethodDelegate> _serviceMethods;

    private delegate Task<Result<CipherPayload, NetworkFailure>> GrpcMethodDelegate(
        CipherPayload payload,
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        CancellationToken token
    );

    public UnaryRpcServices(
        MembershipServices.MembershipServicesClient membershipServicesClient,
        AppDeviceServiceActions.AppDeviceServiceActionsClient appDeviceServiceActionsClient,
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient
    )
    {
        _serviceMethods = new Dictionary<RpcServiceType, GrpcMethodDelegate>
        {
            [RpcServiceType.RegisterAppDevice] = RegisterDeviceAsync,
            [RpcServiceType.ValidatePhoneNumber] = ValidatePhoneNumberAsync,
            [RpcServiceType.OpaqueRegistrationInit] = OpaqueRegistrationRecordRequestAsync,
            [RpcServiceType.VerifyOtp] = VerifyCodeAsync,
            [RpcServiceType.OpaqueRegistrationComplete] = OpaqueRegistrationCompleteRequestAsync,
            [RpcServiceType.OpaqueSignInInitRequest] = OpaqueSignInInitRequestAsync,
            [RpcServiceType.OpaqueSignInCompleteRequest] = OpaqueSignInCompleteRequestAsync,
        };
        return;

        async Task<Result<CipherPayload, NetworkFailure>> RegisterDeviceAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    appDeviceServiceActionsClient.RegisterDeviceAppIfNotExistAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<CipherPayload, NetworkFailure>> ValidatePhoneNumberAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    authenticationServicesClient.ValidatePhoneNumberAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueRegistrationRecordRequestAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    membershipServicesClient.OpaqueRegistrationInitRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<CipherPayload, NetworkFailure>> VerifyCodeAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    authenticationServicesClient.VerifyOtpAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueRegistrationCompleteRequestAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    membershipServicesClient.OpaqueRegistrationCompleteRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueSignInInitRequestAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    membershipServicesClient.OpaqueSignInInitRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<CipherPayload, NetworkFailure>> OpaqueSignInCompleteRequestAsync(
            CipherPayload payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    membershipServicesClient.OpaqueSignInCompleteRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }
    }

    public async Task<Result<RpcFlow, NetworkFailure>> InvokeRequestAsync(
        ServiceRequest request,
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        CancellationToken token
    )
    {
        if (_serviceMethods.TryGetValue(request.RpcServiceMethod, out GrpcMethodDelegate? method))
        {
            Result<CipherPayload, NetworkFailure> result = await method(
                request.Payload,
                networkEvents,
                systemEvents,
                token
            );
            return Result<RpcFlow, NetworkFailure>.Ok(
                new RpcFlow.SingleCall(Task.FromResult(result))
            );
        }

        return Result<RpcFlow, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Unknown service type")
        );
    }

    private static async Task<Result<CipherPayload, NetworkFailure>> ExecuteGrpcCallAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        Func<AsyncUnaryCall<CipherPayload>> grpcCallFactory
    )
    {
        try
        {
            AsyncUnaryCall<CipherPayload> call = grpcCallFactory();
            CipherPayload response = await call.ResponseAsync;

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);

            return Result<CipherPayload, NetworkFailure>.Ok(response);
        }
        catch (Exception exc)
        {
            Log.Warning(exc, "gRPC call failed: {Message}", exc.Message);
            await systemEvents.NotifySystemStateAsync(SystemState.DataCenterShutdown);

            return Result<CipherPayload, NetworkFailure>.Err(
                NetworkFailure.DataCenterShutdown(exc.Message)
            );
        }
    }
}