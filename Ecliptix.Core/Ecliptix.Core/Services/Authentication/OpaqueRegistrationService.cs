using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Grpc.Core;
using ReactiveUI;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class OpaqueRegistrationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    IServerPublicKeyProvider serverPublicKeyProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider)
    : IOpaqueRegistrationService, IDisposable
{
    private readonly ConcurrentDictionary<Guid, uint> _activeStreams = new();
    private readonly ConcurrentDictionary<Guid, VerificationPurpose> _activeSessionPurposes = new();

    private readonly ConcurrentDictionary<ByteString, RegistrationResult> _opaqueRegistrationState = new();

    private readonly Lock _opaqueClientLock = new();
    private OpaqueClient? _opaqueClient;

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
                responseSource.TrySetResult(response);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, true, cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<ValidateMobileNumberResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        ValidateMobileNumberResponse identifier = await responseSource.Task.ConfigureAwait(false);
        return Result<ValidateMobileNumberResponse, string>.Ok(identifier);
    }

    public async Task<Result<CheckMobileNumberAvailabilityResponse, string>>
        CheckMobileNumberAvailabilityAsync(
            ByteString mobileNumberIdentifier,
            string deviceIdentifier,
            uint connectId,
            CancellationToken cancellationToken = default)
    {
        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<CheckMobileNumberAvailabilityResponse, string>.Err(
                localizationService[AuthenticationConstants.MobileNumberIdentifierRequiredKey]);
        }

        if (string.IsNullOrEmpty(deviceIdentifier))
        {
            return Result<CheckMobileNumberAvailabilityResponse, string>.Err(
                localizationService[AuthenticationConstants.DeviceIdentifierRequiredKey]);
        }

        CheckMobileNumberAvailabilityRequest request = new()
        {
            MobileNumberId = mobileNumberIdentifier,
            DeviceId = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier))
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
            payload => HandleVerificationStreamResponse(payload, streamConnectId, onCountdownUpdate, purpose),
            true, cancellationToken).ConfigureAwait(false);

        if (!streamResult.IsErr)
        {
            return Result<Unit, string>.Ok(Unit.Value);
        }

        NetworkFailure failure = streamResult.UnwrapErr();
        HandleVerificationStreamFailure(failure, onCountdownUpdate);
        return Result<Unit, string>.Err(GetNetworkFailureMessage(failure));
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

        VerificationPurpose purpose =
            _activeSessionPurposes.GetValueOrDefault(sessionIdentifier, VerificationPurpose.Registration);

        InitiateVerificationRequest request = new()
        {
            MobileNumberIdentifier = mobileNumberIdentifier,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier)),
            Purpose = purpose,
            Type = InitiateVerificationRequest.Types.Type.ResendOtp
        };

        Result<Unit, NetworkFailure> result = await networkProvider.ExecuteReceiveStreamRequestAsync(
            streamConnectId,
            RpcServiceType.InitiateVerification,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
            payload => HandleVerificationStreamResponse(payload, streamConnectId, onCountdownUpdate, purpose),
            true, cancellationToken).ConfigureAwait(false);

        if (!result.IsErr)
        {
            return Result<Unit, string>.Ok(Unit.Value);
        }

        NetworkFailure failure = result.UnwrapErr();
        HandleVerificationStreamFailure(failure, onCountdownUpdate);
        return Result<Unit, string>.Err(GetNetworkFailureMessage(failure));
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

        VerificationPurpose purpose =
            _activeSessionPurposes.GetValueOrDefault(sessionIdentifier, VerificationPurpose.Registration);

        VerifyCodeRequest request = new()
        {
            Code = otpCode,
            Purpose = purpose,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier)),
            StreamConnectId = activeStreamId,
        };

        TaskCompletionSource<Result<Protobuf.Membership.Membership, string>> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.VerifyOtp,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                VerifyCodeResponse response = Helpers.ParseFromBytes<VerifyCodeResponse>(payload);

                if (response.Result == VerificationResult.Succeeded)
                {
                    responseSource.TrySetResult(Result<Protobuf.Membership.Membership, string>.Ok(response.Membership));
                }
                else
                {
                    responseSource.TrySetResult(Result<Protobuf.Membership.Membership, string>.Err(
                        localizationService[AuthenticationConstants.InvalidOtpCodeKey]));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, true, cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(networkResult.UnwrapErr().Message);
        }

        return await responseSource.Task.ConfigureAwait(false);
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

        SensitiveBytes? passwordBytes = null;
        Result<SensitiveBytes, SodiumFailure>? createResult = null;

        try
        {
            secureKey.WithSecureBytes(passwordSpan => { createResult = SensitiveBytes.From(passwordSpan); });

            if (createResult == null || createResult.Value.IsErr)
            {
                string errorMessage = createResult?.IsErr == true
                    ? $"Failed to create secure password buffer: {createResult.Value.UnwrapErr().Message}"
                    : localizationService[AuthenticationConstants.SecureKeyRequiredKey];
                return Result<Unit, string>.Err(errorMessage);
            }

            passwordBytes = createResult.Value.Unwrap();

            if (passwordBytes.Length == 0)
            {
                return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SecureKeyRequiredKey]);
            }

            const int maxFlowAttempts = 3;

            return await RetryAsync(
                    maxFlowAttempts,
                    (attempt, attemptCancellationToken) =>
                        ExecuteCompleteRegistrationAttemptAsync(
                            membershipIdentifier,
                            passwordBytes!,
                            connectId,
                            attempt,
                            attemptCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            passwordBytes?.Dispose();
        }
    }

    public async Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier)
    {
        if (sessionIdentifier == AuthenticationConstants.EmptyGuid)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SessionIdentifierRequiredKey]);
        }

        return await CleanupStreamAsync(sessionIdentifier).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_opaqueClientLock)
        {
            _opaqueClient?.Dispose();
            _opaqueClient = null;
        }
    }

    private static RegistrationAttemptResult CreateAttemptSuccess(RpcRequestContext context) =>
        new(Result<Unit, string>.Ok(Unit.Value), false, context.CorrelationId, context.IdempotencyKey, string.Empty);

    private static RegistrationAttemptResult CreateAttemptFailure(string error, bool isTransient,
        RpcRequestContext context) =>
        new(Result<Unit, string>.Err(error), isTransient, context.CorrelationId, context.IdempotencyKey, error);

    private static bool IsTransientRpcStatus(StatusCode statusCode) =>
        statusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.Cancelled;

    private static bool IsTransientException(Exception exception) =>
        exception switch
        {
            RpcException rpcException when IsTransientRpcStatus(rpcException.StatusCode) => true,
            IOException => true,
            SocketException => true,
            TimeoutException => true,
            _ => false
        };

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

        if (failure.UserError is not { } userError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(userError.I18nKey) &&
            string.Equals(userError.I18nKey, "error.auth_flow_missing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return userError.ErrorCode == ErrorCode.DependencyUnavailable &&
               failure.InnerException is RpcException { StatusCode: StatusCode.NotFound };
    }

    private static string GetNetworkFailureMessage(NetworkFailure failure) =>
        failure.UserError?.Message ?? failure.Message;

    private void HandleVerificationStreamFailure(
        NetworkFailure failure,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate)
    {
        if (IsVerificationSessionMissing(failure))
        {
            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound,
                    AuthenticationConstants.ErrorMessages.SessionExpiredStartOver));
        }

        if (failure.FailureType is NetworkFailureType.DataCenterNotResponding
            or NetworkFailureType.DataCenterShutdown)
        {
            string errorMessage = GetNetworkFailureMessage(failure);
            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable,
                    errorMessage));
        }
    }

    private static bool IsVerificationSessionMissing(NetworkFailure failure)
    {
        if (failure.UserError is not { } userError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(userError.I18nKey) &&
            (string.Equals(userError.I18nKey, AuthenticationConstants.SessionNotFoundKey,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(userError.I18nKey, AuthenticationConstants.VerificationSessionExpiredKey,
                 StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return userError.ErrorCode == ErrorCode.NotFound;
    }

    private readonly record struct RegistrationAttemptResult(
        Result<Unit, string> Outcome,
        bool IsTransient,
        string CorrelationId,
        string IdempotencyKey,
        string FailureMessage);

    private Result<byte[], RegistrationAttemptResult> ValidatePasswordCopy(
        byte[]? passwordCopy,
        RpcRequestContext requestContext)
    {
        if (passwordCopy == null || passwordCopy.Length == 0)
        {
            return Result<byte[], RegistrationAttemptResult>.Err(
                CreateAttemptFailure(
                    localizationService[AuthenticationConstants.SecureKeyRequiredKey],
                    false,
                    requestContext));
        }

        return Result<byte[], RegistrationAttemptResult>.Ok(passwordCopy);
    }

    private Result<RegistrationResult, RegistrationAttemptResult> CreateAndTrackRegistrationState(
        OpaqueClient opaqueClient,
        byte[] passwordCopy,
        ByteString membershipIdentifier,
        RpcRequestContext requestContext)
    {
        RegistrationResult registrationResult = opaqueClient.CreateRegistrationRequest(passwordCopy);

        if (_opaqueRegistrationState.TryAdd(membershipIdentifier, registrationResult))
        {
            return Result<RegistrationResult, RegistrationAttemptResult>.Ok(registrationResult);
        }

        registrationResult.Dispose();
        return Result<RegistrationResult, RegistrationAttemptResult>.Err(
            CreateAttemptFailure(
                localizationService[AuthenticationConstants.RegistrationFailedKey],
                true,
                requestContext));
    }

    private Result<Unit, RegistrationAttemptResult> ProcessInitializationResponse(
        OpaqueRegistrationInitResponse initResponse,
        RegistrationResult registrationState,
        RpcRequestContext requestContext)
    {
        if (initResponse.Result == OpaqueRegistrationInitResponse.Types.UpdateResult.Succeeded)
        {
            return Result<Unit, RegistrationAttemptResult>.Ok(Unit.Value);
        }

        registrationState.Dispose();

        string errorMessage = initResponse.Result switch
        {
            OpaqueRegistrationInitResponse.Types.UpdateResult.InvalidCredentials =>
                localizationService[AuthenticationConstants.InvalidCredentialsKey],
            _ => localizationService[AuthenticationConstants.RegistrationFailedKey]
        };

        return Result<Unit, RegistrationAttemptResult>.Err(
            CreateAttemptFailure(errorMessage, false, requestContext));
    }

    private void CleanupSensitiveRegistrationData(
        byte[]? passwordCopy,
        byte[]? serverRegistrationResponse,
        byte[]? registrationRecord,
        byte[]? masterKey)
    {
        if (passwordCopy is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(passwordCopy);
        }

        if (serverRegistrationResponse is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(serverRegistrationResponse);
        }

        if (registrationRecord is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(registrationRecord);
        }

        if (masterKey is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private async Task<Result<RegistrationAttemptResult, RegistrationAttemptResult>>
        FinalizeAndCompleteRegistrationAsync(
            OpaqueClient opaqueClient,
            OpaqueRegistrationInitResponse initResponse,
            RegistrationResult trackedRegistrationResult,
            ByteString membershipIdentifier,
            uint connectId,
            RpcRequestContext requestContext,
            CancellationToken cancellationToken)
    {
        byte[]? serverRegistrationResponse = new byte[initResponse.PeerOprf.Length];
        byte[]? registrationRecord = null;
        byte[]? masterKey = null;

        try
        {
            initResponse.PeerOprf.Span.CopyTo(serverRegistrationResponse);

            (byte[] record, byte[] generatedMasterKey) =
                opaqueClient.FinalizeRegistration(serverRegistrationResponse, trackedRegistrationResult);
            registrationRecord = record;
            masterKey = generatedMasterKey;

            OpaqueRegistrationCompleteRequest completeRequest = new()
            {
                PeerRegistrationRecord = ByteString.CopyFrom(registrationRecord),
                MembershipIdentifier = membershipIdentifier,
                MasterKey = ByteString.CopyFrom(masterKey)
            };

            TaskCompletionSource<OpaqueRegistrationCompleteResponse> responseSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                    connectId,
                    RpcServiceType.RegistrationComplete,
                    SecureByteStringInterop.WithByteStringAsSpan(completeRequest.ToByteString(),
                        span => span.ToArray()),
                    payload =>
                    {
                        OpaqueRegistrationCompleteResponse response =
                            Helpers.ParseFromBytes<OpaqueRegistrationCompleteResponse>(payload);
                        responseSource.TrySetResult(response);

                        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                    },
                    true,
                    cancellationToken,
                    requestContext: requestContext)
                .ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return Result<RegistrationAttemptResult, RegistrationAttemptResult>.Err(
                    CreateAttemptFailure(
                        FormatNetworkFailure(networkResult.UnwrapErr()),
                        IsTransientRegistrationFailure(networkResult.UnwrapErr()),
                        requestContext));
            }

            OpaqueRegistrationCompleteResponse completeResponse =
                await responseSource.Task.ConfigureAwait(false);

            if (completeResponse.Result != OpaqueRegistrationCompleteResponse.Types.RegistrationResult.Succeeded)
            {
                return Result<RegistrationAttemptResult, RegistrationAttemptResult>.Err(
                    CreateAttemptFailure(
                        localizationService[AuthenticationConstants.RegistrationFailedKey],
                        false,
                        requestContext));
            }

            if (completeResponse.ActiveAccount?.UniqueIdentifier != null)
            {
                await applicationSecureStorageProvider
                    .SetCurrentAccountIdAsync(completeResponse.ActiveAccount.UniqueIdentifier)
                    .ConfigureAwait(false);
            }

            return Result<RegistrationAttemptResult, RegistrationAttemptResult>.Ok(
                CreateAttemptSuccess(requestContext));
        }
        finally
        {
            CleanupSensitiveRegistrationData(null, serverRegistrationResponse, registrationRecord, masterKey);
        }
    }

    private async Task<RegistrationAttemptResult> ExecuteCompleteRegistrationAttemptAsync(
        ByteString membershipIdentifier,
        SensitiveBytes password,
        uint connectId,
        int attempt,
        CancellationToken cancellationToken)
    {
        RpcRequestContext requestContext = RpcRequestContext.CreateNew(attempt);
        bool allowReinit = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using OpaqueClient opaqueClient = new(serverPublicKeyProvider.GetServerPublicKey());

            byte[]? passwordCopy = null;
            RegistrationResult? registrationResult = null;
            RegistrationResult? trackedRegistrationResult = null;

            try
            {
                Result<Unit, SodiumFailure> readPasswordResult = password.WithReadAccess(span =>
                {
                    passwordCopy = span.ToArray();
                    return Result<Unit, SodiumFailure>.Ok(Unit.Value);
                });

                if (readPasswordResult.IsErr)
                {
                    return CreateAttemptFailure(
                        $"Failed to read password: {readPasswordResult.UnwrapErr().Message}",
                        false,
                        requestContext);
                }

                Result<byte[], RegistrationAttemptResult> passwordValidation =
                    ValidatePasswordCopy(passwordCopy, requestContext);

                if (passwordValidation.IsErr)
                {
                    return passwordValidation.UnwrapErr();
                }

                Result<RegistrationResult, RegistrationAttemptResult> stateResult =
                    CreateAndTrackRegistrationState(opaqueClient, passwordValidation.Unwrap(), membershipIdentifier,
                        requestContext);

                if (stateResult.IsErr)
                {
                    return stateResult.UnwrapErr();
                }

                RegistrationResult registrationState = stateResult.Unwrap();
                registrationResult = registrationState;

                Result<OpaqueRegistrationInitResponse, NetworkFailure> initResult =
                    await InitiateOpaqueRegistrationAsync(
                            membershipIdentifier,
                            registrationState.GetRequestCopy(),
                            connectId,
                            requestContext,
                            cancellationToken)
                        .ConfigureAwait(false);

                if (initResult.IsErr)
                {
                    NetworkFailure failure = initResult.UnwrapErr();
                    registrationState.Dispose();
                    registrationResult = null;
                    return CreateAttemptFailure(
                        FormatNetworkFailure(failure),
                        IsTransientRegistrationFailure(failure),
                        requestContext);
                }

                OpaqueRegistrationInitResponse initResponse = initResult.Unwrap();

                Result<Unit, RegistrationAttemptResult> initProcessing =
                    ProcessInitializationResponse(initResponse, registrationState, requestContext);

                if (initProcessing.IsErr)
                {
                    registrationResult = null;
                    return initProcessing.UnwrapErr();
                }

                trackedRegistrationResult = registrationState;
                registrationResult = null;

                Result<RegistrationAttemptResult, RegistrationAttemptResult> finalizeResult =
                    await FinalizeAndCompleteRegistrationAsync(
                        opaqueClient,
                        initResponse,
                        trackedRegistrationResult,
                        membershipIdentifier,
                        connectId,
                        requestContext,
                        cancellationToken).ConfigureAwait(false);

                if (_opaqueRegistrationState.TryRemove(membershipIdentifier,
                        out RegistrationResult? completedResult))
                {
                    completedResult.Dispose();
                }

                trackedRegistrationResult = null;

                if (finalizeResult.IsOk)
                {
                    return finalizeResult.Unwrap();
                }

                RegistrationAttemptResult failureResult = finalizeResult.UnwrapErr();

                if (!allowReinit || !failureResult.IsTransient)
                {
                    return failureResult;
                }

                allowReinit = false;
                requestContext.MarkReinitAttempted();
                Serilog.Log.Information(
                    "[REGISTRATION-FLOW-REINIT] Registration complete failed, retrying with reinit. CorrelationId: {CorrelationId}, IdempotencyKey: {IdempotencyKey}",
                    requestContext.CorrelationId,
                    requestContext.IdempotencyKey);
                continue;
            }
            catch (Exception ex) when (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (trackedRegistrationResult != null &&
                    _opaqueRegistrationState.TryRemove(membershipIdentifier, out RegistrationResult? cachedResult))
                {
                    cachedResult.Dispose();
                    trackedRegistrationResult = null;
                }

                registrationResult?.Dispose();
                registrationResult = null;

                string message = $"{AuthenticationConstants.RegistrationFailurePrefix}{ex.Message}";
                bool isTransient = IsTransientException(ex);

                if (isTransient)
                {
                    Serilog.Log.Warning(ex,
                        "[REGISTRATION-FLOW-RETRY] Exception during registration attempt {Attempt}, transient classification applied. CorrelationId: {CorrelationId}, IdempotencyKey: {IdempotencyKey}",
                        attempt,
                        requestContext.CorrelationId,
                        requestContext.IdempotencyKey);
                }
                else
                {
                    Serilog.Log.Error(ex,
                        "[REGISTRATION-FLOW-FAILURE] Non-transient exception during registration. CorrelationId: {CorrelationId}, IdempotencyKey: {IdempotencyKey}",
                        requestContext.CorrelationId,
                        requestContext.IdempotencyKey);
                }

                return CreateAttemptFailure(message, isTransient, requestContext);
            }
            finally
            {
                registrationResult?.Dispose();
                trackedRegistrationResult?.Dispose();
                CleanupSensitiveRegistrationData(passwordCopy, null, null, null);
            }
        }
    }

    private async Task<Result<Unit, string>> RetryAsync(
        int maxAttempts,
        Func<int, CancellationToken, Task<RegistrationAttemptResult>> attemptFactory,
        CancellationToken cancellationToken)
    {
        RegistrationAttemptResult lastResult = default;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RegistrationAttemptResult attemptResult =
                await attemptFactory(attempt, cancellationToken).ConfigureAwait(false);

            if (attemptResult.Outcome.IsOk)
            {
                return attemptResult.Outcome;
            }

            lastResult = attemptResult;

            if (!attemptResult.IsTransient || attempt == maxAttempts)
            {
                return attemptResult.Outcome;
            }

            Serilog.Log.Warning(
                "[REGISTRATION-FLOW-RETRY] Transient failure during registration. Retrying attempt {NextAttempt}/{MaxAttempts}. CorrelationId: {CorrelationId}, IdempotencyKey: {IdempotencyKey}, Failure: {Failure}",
                attempt + 1,
                maxAttempts,
                attemptResult.CorrelationId,
                attemptResult.IdempotencyKey,
                attemptResult.FailureMessage);
        }

        return lastResult.Outcome;
    }

    private string FormatNetworkFailure(NetworkFailure failure) =>
        $"{AuthenticationConstants.RegistrationFailurePrefix}{(failure.UserError?.Message ?? failure.Message)}";

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

        OpaqueRegistrationInitRequest request = new()
        {
            PeerOprf = ByteString.CopyFrom(registrationRequest), MembershipIdentifier = membershipIdentifier
        };

        TaskCompletionSource<OpaqueRegistrationInitResponse> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.RegistrationInit,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                OpaqueRegistrationInitResponse response =
                    Helpers.ParseFromBytes<OpaqueRegistrationInitResponse>(payload);
                responseSource.TrySetResult(response);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, true, cancellationToken, requestContext: requestContext).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Err(networkResult.UnwrapErr());
        }

        OpaqueRegistrationInitResponse initResponse = await responseSource.Task.ConfigureAwait(false);
        return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Ok(initResponse);
    }

    private void ProcessVerificationUpdate(
        VerificationCountdownUpdate verificationCountdownUpdate,
        Guid verificationIdentifier,
        uint streamConnectId,
        VerificationPurpose purpose,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate)
    {
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
            _activeSessionPurposes.TryAdd(verificationIdentifier, purpose);
        }

        RxApp.MainThreadScheduler.Schedule(() =>
            onCountdownUpdate?.Invoke(
                verificationCountdownUpdate.SecondsRemaining,
                verificationIdentifier,
                verificationCountdownUpdate.Status,
                verificationCountdownUpdate.Message));
    }

    private Task<Result<Unit, NetworkFailure>> HandleVerificationStreamResponse(
        byte[] payload,
        uint streamConnectId,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate,
        VerificationPurpose purpose = VerificationPurpose.Registration)
    {
        VerificationCountdownUpdate verificationCountdownUpdate =
            Helpers.ParseFromBytes<VerificationCountdownUpdate>(payload);

        if (verificationCountdownUpdate.SessionIdentifier == null ||
            verificationCountdownUpdate.SessionIdentifier.IsEmpty)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed,
                    verificationCountdownUpdate.Message));
            return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
        }

        try
        {
            Guid verificationIdentifier = Helpers.FromByteStringToGuid(verificationCountdownUpdate.SessionIdentifier);
            ProcessVerificationUpdate(verificationCountdownUpdate, verificationIdentifier, streamConnectId, purpose,
                onCountdownUpdate);
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
        _activeSessionPurposes.TryRemove(sessionIdentifier, out _);

        if (_activeStreams.TryRemove(sessionIdentifier, out uint streamConnectId))
        {
            Result<Unit, NetworkFailure> cleanupResult =
                await networkProvider.CleanupStreamProtocolAsync(streamConnectId).ConfigureAwait(false);

            if (!cleanupResult.IsErr)
            {
                return Result<Unit, string>.Ok(Unit.Value);
            }

            return Result<Unit, string>.Err(cleanupResult.UnwrapErr().Message);
        }

        return Result<Unit, string>.Ok(Unit.Value);
    }
}
