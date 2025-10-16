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
    private readonly MembershipServices.MembershipServicesClient _membershipServicesClient;

    private delegate Task<Result<SecureEnvelope, NetworkFailure>> GrpcMethodDelegate(
        SecureEnvelope payload,
        INetworkEventService networkEvents,
        CancellationToken token
    );

    public UnaryRpcServices(
        MembershipServices.MembershipServicesClient membershipServicesClient,
        DeviceService.DeviceServiceClient deviceServiceClient,
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient
    )
    {
        _membershipServicesClient = membershipServicesClient;

        _serviceMethods = new Dictionary<RpcServiceType, GrpcMethodDelegate>
        {
            [RpcServiceType.RegisterAppDevice] = RegisterDeviceAsync,
            [RpcServiceType.ValidateMobileNumber] = ValidateMobileNumberAsync,
            [RpcServiceType.CheckMobileNumberAvailability] = CheckMobileNumberAvailabilityAsync,
            [RpcServiceType.OpaqueRegistrationInit] = OpaqueRegistrationRecordRequestAsync,
            [RpcServiceType.VerifyOtp] = VerifyCodeAsync,
            [RpcServiceType.OpaqueRegistrationComplete] = OpaqueRegistrationCompleteRequestAsync,
            [RpcServiceType.OpaqueRecoverySecretKeyInit] = OpaqueRecoveryInitRequestAsync,
            [RpcServiceType.OpaqueRecoverySecretKeyComplete] = OpaqueRecoveryCompleteRequestAsync,
            [RpcServiceType.OpaqueSignInInitRequest] = OpaqueSignInInitRequestAsync,
            [RpcServiceType.OpaqueSignInCompleteRequest] = OpaqueSignInCompleteRequestAsync,
            [RpcServiceType.Logout] = LogoutAsync
        };

        async Task<Result<SecureEnvelope, NetworkFailure>> LogoutAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    _membershipServicesClient.LogoutAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> RegisterDeviceAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    deviceServiceClient.RegisterDeviceAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> ValidateMobileNumberAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    authenticationServicesClient.ValidateMobileNumberAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> CheckMobileNumberAvailabilityAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    authenticationServicesClient.CheckMobileNumberAvailabilityAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRegistrationRecordRequestAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    membershipServicesClient.OpaqueRegistrationInitRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> VerifyCodeAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    authenticationServicesClient.VerifyOtpAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRegistrationCompleteRequestAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    membershipServicesClient.OpaqueRegistrationCompleteRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueSignInInitRequestAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    membershipServicesClient.OpaqueSignInInitRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueSignInCompleteRequestAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    membershipServicesClient.OpaqueSignInCompleteRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRecoveryInitRequestAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    membershipServicesClient.OpaqueRecoverySecretKeyInitRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRecoveryCompleteRequestAsync(
            SecureEnvelope payload,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                () =>
                    membershipServicesClient.OpaqueRecoverySecretKeyCompleteRequestAsync(
                        payload,
                        new CallOptions(cancellationToken: token)
                    )
            ).ConfigureAwait(false);
        }
    }

    public async Task<Result<RpcFlow, NetworkFailure>> InvokeRequestAsync(
        ServiceRequest request,
        INetworkEventService networkEvents,
        CancellationToken token
    )
    {
        if (_serviceMethods.TryGetValue(request.RpcServiceMethod, out GrpcMethodDelegate? method))
        {
            Result<SecureEnvelope, NetworkFailure> result = await method(
                request.Payload,
                networkEvents,
                token
            ).ConfigureAwait(false);

            if (result.IsOk)
            {
                return Result<RpcFlow, NetworkFailure>.Ok(
                    new RpcFlow.SingleCall(Task.FromResult(result))
                );
            }

            return Result<RpcFlow, NetworkFailure>.Err(result.UnwrapErr());
        }

        return Result<RpcFlow, NetworkFailure>.Err(
            NetworkFailure.InvalidRequestType("Unknown service type")
        );
    }

    private static async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteGrpcCallAsync(
        INetworkEventService networkEvents,
        Func<AsyncUnaryCall<SecureEnvelope>> grpcCallFactory
    )
    {
        try
        {
            AsyncUnaryCall<SecureEnvelope> call = grpcCallFactory();
            SecureEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected).ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            NetworkFailure failure = await GrpcErrorHandler.ClassifyRpcExceptionWithEventsAsync(
                rpcEx, networkEvents).ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }
}
