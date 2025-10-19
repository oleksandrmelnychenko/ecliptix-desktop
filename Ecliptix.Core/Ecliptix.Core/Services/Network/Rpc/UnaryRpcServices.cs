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
    private readonly IGrpcErrorProcessor _errorProcessor;

    private delegate Task<Result<SecureEnvelope, NetworkFailure>> GrpcMethodDelegate(
        SecureEnvelope payload,
        RpcRequestContext? requestContext,
        INetworkEventService networkEvents,
        CancellationToken token
    );

    public UnaryRpcServices(
        MembershipServices.MembershipServicesClient membershipServicesClient,
        DeviceService.DeviceServiceClient deviceServiceClient,
        AuthVerificationServices.AuthVerificationServicesClient authenticationServicesClient,
        IGrpcErrorProcessor errorProcessor
    )
    {
        _membershipServicesClient = membershipServicesClient;
        _errorProcessor = errorProcessor;

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
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    _membershipServicesClient.LogoutAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> RegisterDeviceAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    deviceServiceClient.RegisterDeviceAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> ValidateMobileNumberAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    authenticationServicesClient.ValidateMobileNumberAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> CheckMobileNumberAvailabilityAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    authenticationServicesClient.CheckMobileNumberAvailabilityAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRegistrationRecordRequestAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    membershipServicesClient.OpaqueRegistrationInitRequestAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> VerifyCodeAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    authenticationServicesClient.VerifyOtpAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRegistrationCompleteRequestAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    membershipServicesClient.OpaqueRegistrationCompleteRequestAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueSignInInitRequestAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    membershipServicesClient.OpaqueSignInInitRequestAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueSignInCompleteRequestAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    membershipServicesClient.OpaqueSignInCompleteRequestAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRecoveryInitRequestAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    membershipServicesClient.OpaqueRecoverySecretKeyInitRequestAsync(
                        payload,
                        callOptions
                    )
            ).ConfigureAwait(false);
        }

        async Task<Result<SecureEnvelope, NetworkFailure>> OpaqueRecoveryCompleteRequestAsync(
            SecureEnvelope payload,
            RpcRequestContext? requestContext,
            INetworkEventService networkEvents,
            CancellationToken token
        )
        {
            return await ExecuteGrpcCallAsync(
                networkEvents,
                requestContext,
                token,
                callOptions =>
                    membershipServicesClient.OpaqueRecoverySecretKeyCompleteRequestAsync(
                        payload,
                        callOptions
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
                request.RequestContext,
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

    private async Task<Result<SecureEnvelope, NetworkFailure>> ExecuteGrpcCallAsync(
        INetworkEventService networkEvents,
        RpcRequestContext? requestContext,
        CancellationToken token,
        Func<CallOptions, AsyncUnaryCall<SecureEnvelope>> grpcCallFactory
    )
    {
        try
        {
            Metadata? headers = null;
            if (requestContext != null)
            {
                headers = new Metadata
                {
                    { "x-correlation-id", requestContext.CorrelationId },
                    { "x-idempotency-key", requestContext.IdempotencyKey }
                };
            }

            CallOptions callOptions = new(headers, cancellationToken: token);
            AsyncUnaryCall<SecureEnvelope> call = grpcCallFactory(callOptions);
            SecureEnvelope response = await call.ResponseAsync.ConfigureAwait(false);

            await networkEvents.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected).ConfigureAwait(false);

            return Result<SecureEnvelope, NetworkFailure>.Ok(response);
        }
        catch (RpcException rpcEx)
        {
            NetworkFailure failure = await _errorProcessor.ProcessAsync(rpcEx, networkEvents).ConfigureAwait(false);
            return Result<SecureEnvelope, NetworkFailure>.Err(failure);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }
}
