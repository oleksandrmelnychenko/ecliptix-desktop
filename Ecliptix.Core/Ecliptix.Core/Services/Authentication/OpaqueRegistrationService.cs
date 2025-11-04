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
    private Option<OpaqueClient> _opaqueClient = Option<OpaqueClient>.None;

    public async Task<Result<ValidateMobileNumberResponse, string>> ValidateMobileNumberAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            return Result<ValidateMobileNumberResponse, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_REQUIRED_KEY]);
        }

        ValidateMobileNumberRequest request = new() { MobileNumber = mobileNumber };

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
            uint connectId,
            CancellationToken cancellationToken = default)
    {
        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<CheckMobileNumberAvailabilityResponse, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        CheckMobileNumberAvailabilityRequest request = new() { MobileNumberId = mobileNumberIdentifier };

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
        VerificationPurpose purpose = VerificationPurpose.Registration,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        Result<uint, NetworkFailure> protocolResult =
            await networkProvider.EnsureProtocolForTypeAsync(
                PubKeyExchangeType.ServerStreaming).ConfigureAwait(false);

        if (protocolResult.IsErr)
        {
            return Result<Unit, string>.Err(
                $"{AuthenticationConstants.VERIFICATION_FAILURE_PREFIX}{protocolResult.UnwrapErr().Message}");
        }

        uint streamConnectId = protocolResult.Unwrap();

        InitiateVerificationRequest request = new()
        {
            MobileNumberIdentifier = mobileNumberIdentifier,
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
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default)
    {
        if (sessionIdentifier == AuthenticationConstants.EmptyGuid)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.SESSION_IDENTIFIER_REQUIRED_KEY]);
        }

        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        if (!_activeStreams.TryGetValue(sessionIdentifier, out uint streamConnectId))
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY]);
        }

        VerificationPurpose purpose =
            _activeSessionPurposes.GetValueOrDefault(sessionIdentifier, VerificationPurpose.Registration);

        InitiateVerificationRequest request = new()
        {
            MobileNumberIdentifier = mobileNumberIdentifier,
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
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(otpCode) || otpCode.Length != 6)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(
                localizationService[AuthenticationConstants.INVALID_OTP_CODE_KEY]);
        }

        if (!_activeStreams.TryGetValue(sessionIdentifier, out uint activeStreamId))
        {
            return Result<Protobuf.Membership.Membership, string>.Err(
                localizationService[AuthenticationConstants.NO_ACTIVE_VERIFICATION_SESSION_KEY]);
        }

        VerificationPurpose purpose =
            _activeSessionPurposes.GetValueOrDefault(sessionIdentifier, VerificationPurpose.Registration);

        VerifyCodeRequest request = new() { Code = otpCode, Purpose = purpose, StreamConnectId = activeStreamId };

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
                        localizationService[AuthenticationConstants.INVALID_OTP_CODE_KEY]));
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
                localizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        if (secureKey.Length == 0)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY]);
        }

        SensitiveBytes? secureKeyBytes = null;
        Result<SensitiveBytes, SodiumFailure>? createResult = null;

        try
        {
            secureKey.WithSecureBytes(secureKeySpan => { createResult = SensitiveBytes.From(secureKeySpan); });

            if (createResult == null || createResult.Value.IsErr)
            {
                string errorMessage = createResult?.IsErr is true
                    ? $"Failed to create secure key buffer: {createResult.Value.UnwrapErr().Message}"
                    : localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY];
                return Result<Unit, string>.Err(errorMessage);
            }

            secureKeyBytes = createResult.Value.Unwrap();

            if (secureKeyBytes.Length == 0)
            {
                return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY]);
            }

            const int maxFlowAttempts = 3;

            Result<Unit, string> result = await RetryAsync(
                    maxFlowAttempts,
                    (attempt, attemptCancellationToken) =>
                        ExecuteCompleteRegistrationAttemptAsync(
                            membershipIdentifier,
                            secureKeyBytes!,
                            connectId,
                            attempt,
                            attemptCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        finally
        {
            secureKeyBytes?.Dispose();
        }
    }

    public async Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier)
    {
        if (sessionIdentifier == AuthenticationConstants.EmptyGuid)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.SESSION_IDENTIFIER_REQUIRED_KEY]);
        }

        return await CleanupStreamAsync(sessionIdentifier).ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_opaqueClientLock)
        {
            _opaqueClient.Do(client => client.Dispose());
            _opaqueClient = Option<OpaqueClient>.None;
        }
    }

    private static RegistrationAttemptResult CreateAttemptSuccess() =>
        new(Result<Unit, string>.Ok(Unit.Value), false);

    private static RegistrationAttemptResult CreateAttemptFailure(string error, bool isTransient) =>
        new(Result<Unit, string>.Err(error), isTransient);

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

    private static string GetNetworkFailureMessage(NetworkFailure failure) =>
        failure.UserError?.Message ?? failure.Message;

    private static void HandleVerificationStreamFailure(
        NetworkFailure failure,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate)
    {
        if (IsVerificationSessionMissing(failure))
        {
            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound,
                    AuthenticationConstants.ErrorMessages.SESSION_EXPIRED_START_OVER));
        }

        if (failure.FailureType is not (NetworkFailureType.DataCenterNotResponding
            or NetworkFailureType.DataCenterShutdown))
        {
            return;
        }

        string errorMessage = GetNetworkFailureMessage(failure);
        RxApp.MainThreadScheduler.Schedule(() =>
            onCountdownUpdate?.Invoke(0, Guid.Empty,
                VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable,
                errorMessage));
    }

    private static bool IsVerificationSessionMissing(NetworkFailure failure)
    {
        if (failure.UserError is not { } userError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(userError.I_18N_KEY) &&
            (string.Equals(userError.I_18N_KEY, AuthenticationConstants.SESSION_NOT_FOUND_KEY,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(userError.I_18N_KEY, AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY,
                 StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return userError.ERROR_CODE == ErrorCode.NOT_FOUND;
    }

    private readonly record struct RegistrationAttemptResult(
        Result<Unit, string> Outcome,
        bool IsTransient);

    private Result<byte[], RegistrationAttemptResult> ValidateSecureKeyCopy(
        byte[]? secureKeyCopy)
    {
        if (secureKeyCopy == null || secureKeyCopy.Length == 0)
        {
            return Result<byte[], RegistrationAttemptResult>.Err(
                CreateAttemptFailure(
                    localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY],
                    false));
        }

        return Result<byte[], RegistrationAttemptResult>.Ok(secureKeyCopy);
    }

    private Result<RegistrationResult, RegistrationAttemptResult> CreateAndTrackRegistrationState(
        OpaqueClient opaqueClient,
        byte[] secureKeyCopy,
        ByteString membershipIdentifier)
    {
        RegistrationResult registrationResult = opaqueClient.CreateRegistrationRequest(secureKeyCopy);

        if (_opaqueRegistrationState.TryAdd(membershipIdentifier, registrationResult))
        {
            return Result<RegistrationResult, RegistrationAttemptResult>.Ok(registrationResult);
        }

        registrationResult.Dispose();
        return Result<RegistrationResult, RegistrationAttemptResult>.Err(
            CreateAttemptFailure(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY],
                true));
    }

    private Result<Unit, RegistrationAttemptResult> ProcessInitializationResponse(
        OpaqueRegistrationInitResponse initResponse,
        RegistrationResult registrationState)
    {
        if (initResponse.Result == OpaqueRegistrationInitResponse.Types.UpdateResult.Succeeded)
        {
            return Result<Unit, RegistrationAttemptResult>.Ok(Unit.Value);
        }

        registrationState.Dispose();

        string errorMessage = initResponse.Result switch
        {
            OpaqueRegistrationInitResponse.Types.UpdateResult.InvalidCredentials =>
                localizationService[AuthenticationConstants.INVALID_CREDENTIALS_KEY],
            _ => localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]
        };

        return Result<Unit, RegistrationAttemptResult>.Err(
            CreateAttemptFailure(errorMessage, false));
    }

    private static void CleanupSensitiveRegistrationData(
        byte[]? secureKeyCopy,
        byte[]? serverRegistrationResponse,
        byte[]? registrationRecord,
        byte[]? masterKey)
    {
        if (secureKeyCopy is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(secureKeyCopy);
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
        byte[] serverRegistrationResponse = new byte[initResponse.PeerOprf.Length];
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
                        IsTransientRegistrationFailure(networkResult.UnwrapErr())));
            }

            OpaqueRegistrationCompleteResponse completeResponse =
                await responseSource.Task.ConfigureAwait(false);

            if (completeResponse.Result != OpaqueRegistrationCompleteResponse.Types.RegistrationResult.Succeeded)
            {
                return Result<RegistrationAttemptResult, RegistrationAttemptResult>.Err(
                    CreateAttemptFailure(
                        localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY],
                        false));
            }

            if (completeResponse.ActiveAccount?.UniqueIdentifier != null)
            {
                await applicationSecureStorageProvider
                    .SetCurrentAccountIdAsync(completeResponse.ActiveAccount.UniqueIdentifier)
                    .ConfigureAwait(false);
            }

            return Result<RegistrationAttemptResult, RegistrationAttemptResult>.Ok(
                CreateAttemptSuccess());
        }
        finally
        {
            CleanupSensitiveRegistrationData(null, serverRegistrationResponse, registrationRecord, masterKey);
        }
    }

    private async Task<RegistrationAttemptResult> ExecuteCompleteRegistrationAttemptAsync(
        ByteString membershipIdentifier,
        SensitiveBytes secureKey,
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

            RegistrationAttemptResult result = await TryExecuteRegistrationCycleAsync(
                opaqueClient,
                membershipIdentifier,
                secureKey,
                connectId,
                requestContext,
                cancellationToken).ConfigureAwait(false);

            if (result.Outcome.IsOk || !allowReinit || !result.IsTransient)
            {
                return result;
            }

            allowReinit = false;
            requestContext.MarkReinitAttempted();
        }
    }

    private async Task<RegistrationAttemptResult> TryExecuteRegistrationCycleAsync(
        OpaqueClient opaqueClient,
        ByteString membershipIdentifier,
        SensitiveBytes secureKey,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        byte[]? secureKeyCopy = null;
        RegistrationResult? registrationResult = null;
        RegistrationResult? trackedRegistrationResult = null;

        try
        {
            Result<SecureKeyPreparationResult, RegistrationAttemptResult> prepareResult =
                PrepareSecureKeyForRegistration(secureKey);

            if (prepareResult.IsErr)
            {
                return prepareResult.UnwrapErr();
            }

            secureKeyCopy = prepareResult.Unwrap().SecureKeyCopy;

            Result<RegistrationResult, RegistrationAttemptResult> stateResult =
                CreateAndTrackRegistrationState(opaqueClient, secureKeyCopy, membershipIdentifier);

            if (stateResult.IsErr)
            {
                return stateResult.UnwrapErr();
            }

            registrationResult = stateResult.Unwrap();

            RegistrationAttemptResult attemptResult = await ExecuteRegistrationWorkflowAsync(
                opaqueClient,
                membershipIdentifier,
                registrationResult,
                connectId,
                requestContext,
                cancellationToken).ConfigureAwait(false);

            if (!attemptResult.Outcome.IsOk)
            {
                return attemptResult;
            }

            registrationResult = null;
            CleanupTrackedRegistration(membershipIdentifier, out trackedRegistrationResult);

            return attemptResult;
        }
        catch (Exception ex) when (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleRegistrationException(membershipIdentifier,
                ref trackedRegistrationResult, ref registrationResult);
            return CreateAttemptFailure(
                $"{AuthenticationConstants.REGISTRATION_FAILURE_PREFIX}{ex.Message}",
                IsTransientException(ex));
        }
        finally
        {
            registrationResult?.Dispose();
            trackedRegistrationResult?.Dispose();
            CleanupSensitiveRegistrationData(secureKeyCopy, null, null, null);
        }
    }

    private Result<SecureKeyPreparationResult, RegistrationAttemptResult> PrepareSecureKeyForRegistration(
        SensitiveBytes secureKey)
    {
        byte[]? secureKeyCopy = null;

        Result<Unit, SodiumFailure> readSecureKeyResult = secureKey.WithReadAccess(span =>
        {
            secureKeyCopy = span.ToArray();
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);
        });

        if (readSecureKeyResult.IsErr)
        {
            return Result<SecureKeyPreparationResult, RegistrationAttemptResult>.Err(
                CreateAttemptFailure(
                    $"Failed to read secure key: {readSecureKeyResult.UnwrapErr().Message}",
                    false));
        }

        Result<byte[], RegistrationAttemptResult> validationResult =
            ValidateSecureKeyCopy(secureKeyCopy);

        if (validationResult.IsErr)
        {
            return Result<SecureKeyPreparationResult, RegistrationAttemptResult>.Err(validationResult.UnwrapErr());
        }

        return Result<SecureKeyPreparationResult, RegistrationAttemptResult>.Ok(
            new SecureKeyPreparationResult(secureKeyCopy!));
    }

    private readonly record struct SecureKeyPreparationResult(byte[] SecureKeyCopy);

    private async Task<RegistrationAttemptResult> ExecuteRegistrationWorkflowAsync(
        OpaqueClient opaqueClient,
        ByteString membershipIdentifier,
        RegistrationResult registrationState,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
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
            return CreateAttemptFailure(
                FormatNetworkFailure(failure),
                IsTransientRegistrationFailure(failure));
        }

        OpaqueRegistrationInitResponse initResponse = initResult.Unwrap();

        Result<Unit, RegistrationAttemptResult> initProcessing =
            ProcessInitializationResponse(initResponse, registrationState);

        if (initProcessing.IsErr)
        {
            return initProcessing.UnwrapErr();
        }

        Result<RegistrationAttemptResult, RegistrationAttemptResult> finalizeResult =
            await FinalizeAndCompleteRegistrationAsync(
                opaqueClient,
                initResponse,
                registrationState,
                membershipIdentifier,
                connectId,
                requestContext,
                cancellationToken).ConfigureAwait(false);

        return finalizeResult.IsOk ? finalizeResult.Unwrap() : finalizeResult.UnwrapErr();
    }

    private void CleanupTrackedRegistration(ByteString membershipIdentifier, out RegistrationResult? trackedResult)
    {
        if (_opaqueRegistrationState.TryRemove(membershipIdentifier, out RegistrationResult? completedResult))
        {
            completedResult.Dispose();
        }

        trackedResult = null;
    }

    private void HandleRegistrationException(ByteString membershipIdentifier,
        ref RegistrationResult? trackedResult,
        ref RegistrationResult? registrationResult)
    {
        if (trackedResult != null &&
            _opaqueRegistrationState.TryRemove(membershipIdentifier, out RegistrationResult? cachedResult))
        {
            cachedResult.Dispose();
            trackedResult = null;
        }

        registrationResult?.Dispose();
        registrationResult = null;
    }

    private static async Task<Result<Unit, string>> RetryAsync(
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
        }

        return lastResult.Outcome;
    }

    private static string FormatNetworkFailure(NetworkFailure failure) =>
        $"{AuthenticationConstants.REGISTRATION_FAILURE_PREFIX}{(failure.UserError?.Message ?? failure.Message)}";

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
                    localizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]));
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
        if (ShouldCleanupVerificationStream(verificationCountdownUpdate.Status))
        {
            if (verificationIdentifier != Guid.Empty)
            {
                ScheduleStreamCleanupAsync(verificationIdentifier);
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

    private static bool
        ShouldCleanupVerificationStream(VerificationCountdownUpdate.Types.CountdownUpdateStatus status) =>
        status is VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed
            or VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached
            or VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound;

    private void ScheduleStreamCleanupAsync(Guid verificationIdentifier)
    {
        _ = Task.Run(async () =>
        {
            await CleanupStreamAsync(verificationIdentifier).ConfigureAwait(false);
        }, CancellationToken.None).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception, "[VERIFICATION-CLEANUP] Unhandled exception in cleanup task");
                }
            },
            TaskScheduler.Default);
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
                    $"{AuthenticationConstants.NETWORK_FAILURE_PREFIX}{ex.Message}")));
        }
    }

    private async Task<Result<Unit, string>> CleanupStreamAsync(Guid sessionIdentifier)
    {
        _activeSessionPurposes.TryRemove(sessionIdentifier, out _);

        if (!_activeStreams.TryRemove(sessionIdentifier, out uint streamConnectId))
        {
            return Result<Unit, string>.Ok(Unit.Value);
        }

        Result<Unit, NetworkFailure> cleanupResult =
            await networkProvider.CleanupStreamProtocolAsync(streamConnectId).ConfigureAwait(false);

        return !cleanupResult.IsErr
            ? Result<Unit, string>.Ok(Unit.Value)
            : Result<Unit, string>.Err(cleanupResult.UnwrapErr().Message);
    }
}
