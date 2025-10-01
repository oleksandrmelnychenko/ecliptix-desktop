using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Core.Services.Network.Rpc;

public sealed class UnaryRpcServices : IUnaryRpcServices
{
    private readonly Dictionary<RpcServiceType, GrpcMethodDelegate> _serviceMethods;

    private delegate Task<Result<SecureEnvelope, NetworkFailure>> GrpcMethodDelegate(
        SecureEnvelope payload,
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        CancellationToken token
    );

    public UnaryRpcServices(
        MembershipServices.MembershipServicesClient membershipServicesClient,
        DeviceService.DeviceServiceClient deviceServiceClient,
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

        async Task<Result<SecureEnvelope, NetworkFailure>> RegisterDeviceAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            ISystemEventService systemEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                systemEvents,
                () =>
                    deviceServiceClient.RegisterDeviceAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            );
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> ValidatePhoneNumberAsync(
            SecureEnvelope payload,
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

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRegistrationRecordRequestAsync(
            SecureEnvelope payload,
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

        async Task<Result<SecureEnvelope, NetworkFailure>> VerifyCodeAsync(
            SecureEnvelope payload,
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

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRegistrationCompleteRequestAsync(
            SecureEnvelope payload,
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

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueSignInInitRequestAsync(
            SecureEnvelope payload,
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

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueSignInCompleteRequestAsync(
            SecureEnvelope payload,
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
            Result<SecureEnvelope, NetworkFailure> result = await method(
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

    private static async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteGrpcCallAsync(
        INetworkEventService networkEvents,
        ISystemEventService systemEvents,
        Func<AsyncUnaryCall<SecureEnvelope>> grpcCallFactory
    )
    {
        try
        {
            AsyncUnaryCall<SecureEnvelope> call = grpcCallFactory();
            SecureEnvelope response = await call.ResponseAsync;

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            NetworkFailure failure = await GrpcErrorHandler.ClassifyRpcExceptionWithEventsAsync(
                rpcEx, networkEvents, systemEvents);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }
}