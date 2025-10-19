using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Ecliptix.Core.Services.Authentication.Constants;
using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using ReactiveUI;
using Ecliptix.Opaque.Protocol;
using Unit = Ecliptix.Utilities.Unit;
using Grpc.Core;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class OpaqueRegistrationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    IServerPublicKeyProvider serverPublicKeyProvider)
    : IOpaqueRegistrationService, IDisposable
{
    private readonly ConcurrentDictionary<Guid, uint> _activeStreams = new();

    private readonly ConcurrentDictionary<ByteString, RegistrationResult> _opaqueRegistrationState = new();

    private readonly Lock _opaqueClientLock = new();
    private OpaqueClient? _opaqueClient;
    private byte[]? _cachedServerPublicKey;

    public async Task<Result<ValidateMobileNumberResponse, string>> ValidateMobileNumberAsync(
        string mobileNumber,
        string deviceIdentifier,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            return Result<ValidateMobileNumberResponse, string>.Err(
                localizationService[AuthenticationConstants.MobileNumberRequiredKey]);
        }

        ValidateMobileNumberRequest request = new()
        {
            MobileNumber = mobileNumber,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier))
        };

        TaskCompletionSource<ValidateMobileNumberResponse> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.ValidateMobileNumber,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                ValidateMobileNumberResponse response = Helpers.ParseFromBytes<ValidateMobileNumberResponse>(payload);

                if (response.Result == VerificationResult.InvalidMobile)
                {
                    responseSource.TrySetException(new InvalidOperationException(response.Message));
                }
                else
                {
                    responseSource.TrySetResult(response);
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, true, cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<ValidateMobileNumberResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        ValidateMobileNumberResponse identifier = await responseSource.Task.ConfigureAwait(false);
        return Result<ValidateMobileNumberResponse, string>.Ok(identifier);
    }

    public async Task<Result<CheckMobileNumberAvailabilityResponse, string>> CheckMobileNumberAvailabilityAsync(
        ByteString mobileNumberIdentifier, uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<CheckMobileNumberAvailabilityResponse, string>.Err(
                localizationService[AuthenticationConstants.MobileNumberIdentifierRequiredKey]);
        }

        CheckMobileNumberAvailabilityRequest request = new()
        {
            MobileNumberIdentifier = mobileNumberIdentifier
        };

        TaskCompletionSource<CheckMobileNumberAvailabilityResponse> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.CheckMobileNumberAvailability,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                CheckMobileNumberAvailabilityResponse response =
                    Helpers.ParseFromBytes<CheckMobileNumberAvailabilityResponse>(payload);
                responseSource.TrySetResult(response);
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, true, cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<CheckMobileNumberAvailabilityResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        CheckMobileNumberAvailabilityResponse statusResponse = await responseSource.Task.ConfigureAwait(false);
        return Result<CheckMobileNumberAvailabilityResponse, string>.Ok(statusResponse);
    }

    public async Task<Result<Unit, string>> InitiateOtpVerificationAsync(
        ByteString mobileNumberIdentifier,
        string deviceIdentifier,
        VerificationPurpose purpose = VerificationPurpose.Registration,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MobileNumberIdentifierRequiredKey]);
        }

        if (string.IsNullOrEmpty(deviceIdentifier))
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.DeviceIdentifierRequiredKey]);
        }

        //TODO: FIX second param.
        Result<uint, NetworkFailure> protocolResult =
            await networkProvider.EnsureProtocolForTypeAsync(
                PubKeyExchangeType.ServerStreaming).ConfigureAwait(false);

        if (protocolResult.IsErr)
        {
            return Result<Unit, string>.Err(
                $"{AuthenticationConstants.VerificationFailurePrefix}{protocolResult.UnwrapErr().Message}");
        }

        uint streamConnectId = protocolResult.Unwrap();

        InitiateVerificationRequest request = new()
        {
            MobileNumberIdentifier = mobileNumberIdentifier,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier)),
            Purpose = purpose,
            Type = InitiateVerificationRequest.Types.Type.SendOtp
        };

        Result<Unit, NetworkFailure> streamResult = await networkProvider.ExecuteReceiveStreamRequestAsync(
            streamConnectId,
            RpcServiceType.InitiateVerification,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
            payload => HandleVerificationStreamResponse(payload, streamConnectId, onCountdownUpdate),
            true, cancellationToken).ConfigureAwait(false);

        if (streamResult.IsErr)
        {
            NetworkFailure failure = streamResult.UnwrapErr();
            string errorMessage = failure.Message;

            if (errorMessage.Contains(AuthenticationConstants.ErrorMessages.SessionNotFound) ||
                errorMessage.Contains(AuthenticationConstants.ErrorMessages.StartOver))
            {
                RxApp.MainThreadScheduler.Schedule(() =>
                    onCountdownUpdate?.Invoke(0, Guid.Empty,
                        VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound,
                        AuthenticationConstants.ErrorMessages.SessionExpiredStartOver));
            }

            if (failure.FailureType == NetworkFailureType.DataCenterNotResponding ||
                failure.FailureType == NetworkFailureType.DataCenterShutdown)
            {
                RxApp.MainThreadScheduler.Schedule(() =>
                    onCountdownUpdate?.Invoke(0, Guid.Empty,
                        VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable,
                        errorMessage));
            }

            return Result<Unit, string>.Err(errorMessage);
        }

        return Result<Unit, string>.Ok(Unit.Value);
    }

    public async Task<Result<Unit, string>> ResendOtpVerificationAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionIdentifier == AuthenticationConstants.EmptyGuid)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.SessionIdentifierRequiredKey]);
        }

        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MobileNumberIdentifierRequiredKey]);
        }

        if (string.IsNullOrEmpty(deviceIdentifier))
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.DeviceIdentifierRequiredKey]);
        }

        if (!_activeStreams.TryGetValue(sessionIdentifier, out uint streamConnectId))
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.VerificationSessionExpiredKey]);
        }

        InitiateVerificationRequest request = new()
        {
            MobileNumberIdentifier = mobileNumberIdentifier,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier)),
            Purpose = VerificationPurpose.Registration,
            Type = InitiateVerificationRequest.Types.Type.ResendOtp
        };

        Result<Unit, NetworkFailure> result = await networkProvider.ExecuteReceiveStreamRequestAsync(
            streamConnectId,
            RpcServiceType.InitiateVerification,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
            payload => HandleVerificationStreamResponse(payload, streamConnectId, onCountdownUpdate),
            true, cancellationToken).ConfigureAwait(false);

        if (result.IsErr)
        {
            string errorMessage = result.UnwrapErr().Message;

            if (errorMessage.Contains(AuthenticationConstants.ErrorMessages.SessionNotFound) ||
                errorMessage.Contains(AuthenticationConstants.ErrorMessages.StartOver))
            {
                RxApp.MainThreadScheduler.Schedule(() =>
                    onCountdownUpdate?.Invoke(0, Guid.Empty,
                        VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound,
                        AuthenticationConstants.ErrorMessages.SessionExpiredStartOver));
            }

            return Result<Unit, string>.Err(errorMessage);
        }

        return Result<Unit, string>.Ok(Unit.Value);
    }

    public async Task<Result<Protobuf.Membership.Membership, string>> VerifyOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        string deviceIdentifier,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(otpCode) || otpCode.Length != 6)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(
                localizationService[AuthenticationConstants.InvalidOtpCodeKey]);
        }

        if (!_activeStreams.TryGetValue(sessionIdentifier, out uint activeStreamId))
        {
            return Result<Protobuf.Membership.Membership, string>.Err(
                localizationService[AuthenticationConstants.NoActiveVerificationSessionKey]);
        }

        VerifyCodeRequest request = new()
        {
            Code = otpCode,
            Purpose = VerificationPurpose.Registration,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier)),
            StreamConnectId = activeStreamId,
        };

        TaskCompletionSource<Protobuf.Membership.Membership> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.VerifyOtp,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                VerifyCodeResponse response = Helpers.ParseFromBytes<VerifyCodeResponse>(payload);

                if (response.Result == VerificationResult.Succeeded)
                {
                    responseSource.TrySetResult(response.Membership);
                }
                else
                {
                    responseSource.TrySetException(
                        new InvalidOperationException(
                            localizationService[AuthenticationConstants.InvalidOtpCodeKey]));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, true, cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(networkResult.UnwrapErr().Message);
        }

        Protobuf.Membership.Membership membership = await responseSource.Task.ConfigureAwait(false);
        return Result<Protobuf.Membership.Membership, string>.Ok(membership);
    }

    public async Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier,
        SecureTextBuffer secureKey, uint connectId, CancellationToken cancellationToken = default)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
        }

        if (secureKey.Length == 0)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SecureKeyRequiredKey]);
        }

        const int maxFlowAttempts = 3;
        string? lastError = null;

        for (int attempt = 1; attempt <= maxFlowAttempts; attempt++)
        {
            lastError = null;
            RpcRequestContext requestContext = RpcRequestContext.CreateNew();
            bool allowReinit = true;
            bool shouldRetryAttempt = false;

            while (true)
            {
                try
                {
                    using OpaqueClient opaqueClient = new(serverPublicKeyProvider.GetServerPublicKey());

                RegistrationResult? registrationResult = null;
                secureKey.WithSecureBytes(passwordBytes =>
                {
                    registrationResult = opaqueClient.CreateRegistrationRequest(passwordBytes.ToArray());
                });

                if (registrationResult == null)
                {
                    return Result<Unit, string>.Err(localizationService[AuthenticationConstants.RegistrationFailedKey]);
                }

                    Result<OpaqueRegistrationInitResponse, NetworkFailure> initResult =
                        await InitiateOpaqueRegistrationAsync(membershipIdentifier, registrationResult.GetRequestCopy(),
                            connectId,
                            requestContext,
                            cancellationToken).ConfigureAwait(false);

                    if (initResult.IsErr)
                    {
                        NetworkFailure failure = initResult.UnwrapErr();
                        lastError = FormatNetworkFailure(failure);
                        shouldRetryAttempt = IsTransientRegistrationFailure(failure);
                        registrationResult?.Dispose();
                        break;
                    }

                    OpaqueRegistrationInitResponse initResponse = initResult.Unwrap();

                if (initResponse.Result != OpaqueRegistrationInitResponse.Types.UpdateResult.Succeeded)
                {
                    string errorMessage = initResponse.Result switch
                    {
                        OpaqueRegistrationInitResponse.Types.UpdateResult.InvalidCredentials =>
                            localizationService[AuthenticationConstants.InvalidCredentialsKey],
                        _ => localizationService[AuthenticationConstants.RegistrationFailedKey]
                    };
                    return Result<Unit, string>.Err(errorMessage);
                }

                _opaqueRegistrationState.TryAdd(membershipIdentifier, registrationResult);

                byte[] serverRegistrationResponse =
                    SecureByteStringInterop.WithByteStringAsSpan(initResponse.PeerOprf, span => span.ToArray());

                byte[] registrationRecord =
                    opaqueClient.FinalizeRegistration(serverRegistrationResponse, registrationResult);

                OpaqueRegistrationCompleteRequest completeRequest = new()
                {
                    PeerRegistrationRecord = ByteString.CopyFrom(registrationRecord),
                    MembershipIdentifier = membershipIdentifier
                };

                TaskCompletionSource<OpaqueRegistrationCompleteResponse> responseSource = new();

                    Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                    connectId,
                    RpcServiceType.OpaqueRegistrationComplete,
                    SecureByteStringInterop.WithByteStringAsSpan(completeRequest.ToByteString(),
                        span => span.ToArray()),
                    payload =>
                    {
                        OpaqueRegistrationCompleteResponse response =
                            Helpers.ParseFromBytes<OpaqueRegistrationCompleteResponse>(payload);
                        responseSource.TrySetResult(response);

                        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                    }, true, cancellationToken, requestContext: requestContext).ConfigureAwait(false);

                    if (networkResult.IsErr)
                    {
                        NetworkFailure failure = networkResult.UnwrapErr();

                        if (_opaqueRegistrationState.TryRemove(membershipIdentifier,
                                out RegistrationResult? failedResult))
                        {
                            failedResult?.Dispose();
                        }

                        if (allowReinit && ShouldReinit(failure))
                        {
                            allowReinit = false;
                            requestContext.MarkReinitAttempted();
                            Serilog.Log.Information(
                                "[REGISTRATION-FLOW-REINIT] Registration complete failed due to missing flow, rerunning init.");
                            registrationResult?.Dispose();
                            continue;
                        }

                        lastError = FormatNetworkFailure(failure);
                        shouldRetryAttempt = IsTransientRegistrationFailure(failure);
                        break;
                    }

                    OpaqueRegistrationCompleteResponse completeResponse = await responseSource.Task.ConfigureAwait(false);

                    if (_opaqueRegistrationState.TryRemove(membershipIdentifier, out RegistrationResult? completedResult))
                    {
                        completedResult?.Dispose();
                    }

                    if (completeResponse.Result != OpaqueRegistrationCompleteResponse.Types.RegistrationResult.Succeeded)
                    {
                        return Result<Unit, string>.Err(localizationService[AuthenticationConstants.RegistrationFailedKey]);
                    }

                    return Result<Unit, string>.Ok(Unit.Value);
                }
                catch (Exception ex)
                {
                    if (_opaqueRegistrationState.TryRemove(membershipIdentifier, out RegistrationResult? exceptionResult))
                    {
                        exceptionResult?.Dispose();
                    }

                    lastError =
                        $"{AuthenticationConstants.RegistrationFailurePrefix}{ex.Message}";
                    shouldRetryAttempt = attempt < maxFlowAttempts;

                    if (shouldRetryAttempt)
                    {
                        Serilog.Log.Warning(ex,
                            "[REGISTRATION-FLOW-RETRY] Exception during registration, retrying. Attempt {Attempt}/{MaxAttempts}",
                            attempt + 1, maxFlowAttempts);
                        break;
                    }

                    return Result<Unit, string>.Err(lastError);
                }
            }

            if (lastError != null)
            {
                if (shouldRetryAttempt && attempt < maxFlowAttempts)
                {
                    Serilog.Log.Warning(
                        "[REGISTRATION-FLOW-RETRY] Server state lost, restarting registration flow. Attempt {Attempt}/{MaxAttempts}",
                        attempt + 1, maxFlowAttempts);
                    continue;
                }

                return Result<Unit, string>.Err(lastError);
            }
        }

        return Result<Unit, string>.Err(lastError ??
                                        $"{AuthenticationConstants.RegistrationFailurePrefix}Registration flow failed");
    }

    public async Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier)
    {
        if (sessionIdentifier == AuthenticationConstants.EmptyGuid)
        {
        return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SessionIdentifierRequiredKey]);
        }

        return await CleanupStreamAsync(sessionIdentifier).ConfigureAwait(false);
    }

    private string FormatNetworkFailure(NetworkFailure failure)
    {
        string message = failure.UserError?.Message ?? failure.Message;
        return $"{AuthenticationConstants.RegistrationFailurePrefix}{message}";
    }

    private static bool IsTransientRegistrationFailure(NetworkFailure failure)
    {
        return failure.FailureType is NetworkFailureType.DataCenterNotResponding
            or NetworkFailureType.DataCenterShutdown
            or NetworkFailureType.OperationCancelled;
    }

    private static bool ShouldReinit(NetworkFailure failure)
    {
        if (failure.InnerException is RpcException rpcEx && GrpcErrorClassifier.IsAuthFlowMissing(rpcEx))
        {
            return true;
        }

        if (failure.UserError is { } userError)
        {
            if (!string.IsNullOrWhiteSpace(userError.I18nKey) &&
                string.Equals(userError.I18nKey, "error.auth_flow_missing", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (userError.ErrorCode == ErrorCode.DependencyUnavailable &&
                failure.InnerException is RpcException { StatusCode: StatusCode.NotFound })
            {
                return true;
            }
        }

        return false;
    }

    private OpaqueClient GetOrCreateOpaqueClient()
    {
        byte[] serverPublicKey = serverPublicKeyProvider.GetServerPublicKey();

        lock (_opaqueClientLock)
        {
            if (_opaqueClient == null || _cachedServerPublicKey == null ||
                !serverPublicKey.AsSpan().SequenceEqual(_cachedServerPublicKey.AsSpan()))
            {
                _opaqueClient?.Dispose();
                _opaqueClient = new OpaqueClient(serverPublicKey);
                _cachedServerPublicKey = (byte[])serverPublicKey.Clone();
            }

            return _opaqueClient;
        }
    }

    private async Task<Result<OpaqueRegistrationInitResponse, NetworkFailure>> InitiateOpaqueRegistrationAsync(
        ByteString membershipIdentifier,
        byte[] registrationRequest,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(
                    localizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]));
        }

        try
        {
            OpaqueRegistrationInitRequest request = new()
            {
                PeerOprf = ByteString.CopyFrom(registrationRequest),
                MembershipIdentifier = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRegistrationInitResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.OpaqueRegistrationInit,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
                {
                    try
                    {
                        OpaqueRegistrationInitResponse response =
                            Helpers.ParseFromBytes<OpaqueRegistrationInitResponse>(payload);
                        responseSource.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex,
                            "[OPAQUE-REGISTRATION-INIT] Failed to parse OPAQUE registration init response");
                        responseSource.TrySetException(ex);
                    }

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }, true, cancellationToken, requestContext: requestContext).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Err(networkResult.UnwrapErr());
            }

            OpaqueRegistrationInitResponse initResponse = await responseSource.Task.ConfigureAwait(false);
            return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Ok(initResponse);
        }
        catch (Exception ex)
        {
            return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(ex.Message));
        }
    }

    private Task<Result<Unit, NetworkFailure>> HandleVerificationStreamResponse(
        byte[] payload,
        uint streamConnectId,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate)
    {
        VerificationCountdownUpdate verificationCountdownUpdate =
            Helpers.ParseFromBytes<VerificationCountdownUpdate>(payload);

        string message = verificationCountdownUpdate.Message;

        if (verificationCountdownUpdate.SessionIdentifier == null ||
            verificationCountdownUpdate.SessionIdentifier.IsEmpty)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed,
                    message);
            });
            return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
        }

        try
        {
            Guid verificationIdentifier = Helpers.FromByteStringToGuid(verificationCountdownUpdate.SessionIdentifier);

            if (verificationCountdownUpdate.Status is VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed
                or VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached
                or VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound)
            {
                if (verificationIdentifier != Guid.Empty)
                {
                    _ = Task.Run(async () =>
                    {
                        Result<Unit, string> cleanupResult =
                            await CleanupStreamAsync(verificationIdentifier).ConfigureAwait(false);
                        if (cleanupResult.IsErr)
                        {
                            Serilog.Log.Warning(
                                "[VERIFICATION-CLEANUP] Failed to cleanup verification stream. SessionIdentifier: {SessionIdentifier}, Error: {Error}",
                                verificationIdentifier, cleanupResult.UnwrapErr());
                        }
                    }, CancellationToken.None);
                }
            }

            if (verificationIdentifier != Guid.Empty)
            {
                _activeStreams.TryAdd(verificationIdentifier, streamConnectId);
            }

            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(
                    verificationCountdownUpdate.SecondsRemaining,
                    verificationIdentifier,
                    verificationCountdownUpdate.Status, message));

            return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding(
                    $"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
        }
    }

    private async Task<Result<Unit, string>> CleanupStreamAsync(Guid sessionIdentifier)
    {
        if (_activeStreams.TryRemove(sessionIdentifier, out uint streamConnectId))
        {
            Result<Unit, NetworkFailure> cleanupResult =
                await networkProvider.CleanupStreamProtocolAsync(streamConnectId).ConfigureAwait(false);

            if (!cleanupResult.IsErr) return Result<Unit, string>.Ok(Unit.Value);
            return Result<Unit, string>.Err(cleanupResult.UnwrapErr().Message);
        }

        return Result<Unit, string>.Ok(Unit.Value);
    }

    public void Dispose()
    {
        lock (_opaqueClientLock)
        {
            _opaqueClient?.Dispose();
            _opaqueClient = null;
            _cachedServerPublicKey = null;
        }
    }
}
