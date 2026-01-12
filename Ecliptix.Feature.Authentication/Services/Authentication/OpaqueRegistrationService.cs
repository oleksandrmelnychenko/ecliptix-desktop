using System;
using System.IO;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;
using Ecliptix.Feature.Authentication.Services.Abstractions.Security;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Feature.Authentication.Services.Authentication.Internal;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.OPAQUE.Client;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Membership;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Grpc.Core;
using ReactiveUI;
using Serilog;
using Unit = Ecliptix.Utilities.Unit;
using OtpCountdownStatus = Ecliptix.Protobuf.Membership.OtpCountdownUpdate.Types.Status;
using VerificationRequestType = Ecliptix.Protobuf.Membership.OtpVerificationRequest.Types.Type;

namespace Ecliptix.Feature.Authentication.Services.Authentication;

public sealed class OpaqueRegistrationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    IServerPublicKeyProvider serverPublicKeyProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider)
    : IOpaqueRegistrationService, IDisposable
{
    private readonly RegistrationStateManager _stateManager = new();
    private readonly VerificationStreamManager _streamManager = new(networkProvider);

    public async Task<Result<MobileNumberValidateResponse, string>> ValidateMobileNumberAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            return Result<MobileNumberValidateResponse, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_REQUIRED_KEY]);
        }

        MobileNumberValidateRequest request = new() { MobileNumber = mobileNumber };

        TaskCompletionSource<MobileNumberValidateResponse> responseSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.ValidateMobileNumber,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                MobileNumberValidateResponse response = Helpers.ParseFromBytes<MobileNumberValidateResponse>(payload);
                responseSource.TrySetResult(response);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: true, token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<MobileNumberValidateResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        MobileNumberValidateResponse identifier = await responseSource.Task.ConfigureAwait(false);
        return Result<MobileNumberValidateResponse, string>.Ok(identifier);
    }

    public async Task<Result<MobileNumberValidateResponse, string>> ValidateMobileForRecoveryAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            return Result<MobileNumberValidateResponse, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_REQUIRED_KEY]);
        }

        MobileNumberValidateRequest request = new() { MobileNumber = mobileNumber };

        TaskCompletionSource<MobileNumberValidateResponse> responseSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.ValidateMobileForRecovery,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                MobileNumberValidateResponse response = Helpers.ParseFromBytes<MobileNumberValidateResponse>(payload);
                responseSource.TrySetResult(response);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: true, token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<MobileNumberValidateResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        MobileNumberValidateResponse identifier = await responseSource.Task.ConfigureAwait(false);
        return Result<MobileNumberValidateResponse, string>.Ok(identifier);
    }

    public async Task<Result<MobileNumberAvailabilityResponse, string>>
        CheckMobileNumberAvailabilityAsync(
            ByteString mobileNumberIdentifier,
            uint connectId,
            CancellationToken cancellationToken = default)
    {
        if (mobileNumberIdentifier.IsEmpty)
        {
            return Result<MobileNumberAvailabilityResponse, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        MobileNumberAvailabilityRequest request = new() { MobileNumberId = mobileNumberIdentifier };

        TaskCompletionSource<MobileNumberAvailabilityResponse> responseSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.CheckMobileNumberAvailability,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                MobileNumberAvailabilityResponse response =
                    Helpers.ParseFromBytes<MobileNumberAvailabilityResponse>(payload);
                responseSource.TrySetResult(response);
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: true, token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<MobileNumberAvailabilityResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        MobileNumberAvailabilityResponse statusResponse = await responseSource.Task.ConfigureAwait(false);
        return Result<MobileNumberAvailabilityResponse, string>.Ok(statusResponse);
    }

    public async Task<Result<Unit, string>> InitiateOtpVerificationAsync(
        ByteString mobileNumberIdentifier,
        OtpVerificationPurpose purpose = OtpVerificationPurpose.Registration,
        Action<uint, Guid, OtpCountdownStatus, string?>? onCountdownUpdate = null,
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

        OtpVerificationRequest request = new()
        {
            MobileNumberId = mobileNumberIdentifier,
            Purpose = purpose,
            Type = VerificationRequestType.OtpRequestTypeSend
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
        Action<uint, Guid, OtpCountdownStatus, string?>? onCountdownUpdate = null,
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

        if (!_streamManager.TryGetActiveStream(sessionIdentifier, out uint streamConnectId))
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY]);
        }

        OtpVerificationPurpose purpose = _streamManager.GetSessionPurpose(sessionIdentifier);

        OtpVerificationRequest request = new()
        {
            MobileNumberId = mobileNumberIdentifier,
            Purpose = purpose,
            Type = VerificationRequestType.OtpRequestTypeResend
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

    public async Task<Result<MembershipProto, string>> VerifyOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(otpCode) || otpCode.Length != 6)
        {
            return Result<MembershipProto, string>.Err(
                localizationService[AuthenticationConstants.INVALID_OTP_CODE_KEY]);
        }

        if (!_streamManager.TryGetActiveStream(sessionIdentifier, out uint activeStreamId))
        {
            return Result<MembershipProto, string>.Err(
                localizationService[AuthenticationConstants.NO_ACTIVE_VERIFICATION_SESSION_KEY]);
        }

        OtpVerificationPurpose purpose = _streamManager.GetSessionPurpose(sessionIdentifier);

        OtpCodeVerifyRequest request = new() { Code = otpCode, Purpose = purpose, StreamConnectId = activeStreamId };

        TaskCompletionSource<Result<MembershipProto, string>> responseSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.VerifyOtp,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                OtpCodeVerifyResponse response = Helpers.ParseFromBytes<OtpCodeVerifyResponse>(payload);

                if (response.Result == OtpVerificationResult.Succeeded && response.Membership != null)
                {
                    responseSource.TrySetResult(Result<MembershipProto, string>.Ok(response.Membership));
                }
                else
                {
                    string errorMessage = !string.IsNullOrEmpty(response.Message)
                        ? response.Message
                        : localizationService[AuthenticationConstants.INVALID_OTP_CODE_KEY];
                    responseSource.TrySetResult(Result<MembershipProto, string>.Err(errorMessage));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: true, token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<MembershipProto, string>.Err(networkResult.UnwrapErr().Message);
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

        Result<SensitiveBytes, SodiumFailure> createResult = default;
        secureKey.WithSecureBytes(secureKeySpan => { createResult = SensitiveBytes.From(secureKeySpan); });

        if (createResult.IsErr)
        {
            string errorMessage = $"Failed to create secure key buffer: {createResult.UnwrapErr().Message}";
            return Result<Unit, string>.Err(errorMessage);
        }

        SensitiveBytes secureKeyBytes = createResult.Unwrap();

        try
        {
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
                            secureKeyBytes,
                            connectId,
                            attempt,
                            attemptCancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            return result;
        }
        finally
        {
            secureKeyBytes.Dispose();
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
        _streamManager.Dispose();
        _stateManager.Dispose();
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
        return failure.FailureType is NetworkFailureType.DATA_CENTER_NOT_RESPONDING
            or NetworkFailureType.DATA_CENTER_SHUTDOWN
            or NetworkFailureType.OPERATION_CANCELLED;
    }

    private static string GetNetworkFailureMessage(NetworkFailure failure) =>
        failure.UserError?.Message ?? failure.Message;

    private static void HandleVerificationStreamFailure(
        NetworkFailure failure,
        Action<uint, Guid, OtpCountdownStatus, string?>? onCountdownUpdate)
    {
        if (IsVerificationSessionMissing(failure))
        {
            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    OtpCountdownStatus.OtpCountdownStatusNotFound,
                    AuthenticationConstants.ErrorMessages.SESSION_EXPIRED_START_OVER));
        }

        if (failure.FailureType is not (NetworkFailureType.DATA_CENTER_NOT_RESPONDING
            or NetworkFailureType.DATA_CENTER_SHUTDOWN))
        {
            return;
        }

        string errorMessage = GetNetworkFailureMessage(failure);
        RxApp.MainThreadScheduler.Schedule(() =>
            onCountdownUpdate?.Invoke(0, Guid.Empty,
                OtpCountdownStatus.OtpCountdownStatusServerUnavailable,
                errorMessage));
    }

    private static bool IsVerificationSessionMissing(NetworkFailure failure)
    {
        if (failure.UserError is not { } userError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(userError.I18NKey) &&
            (string.Equals(userError.I18NKey, AuthenticationConstants.SESSION_NOT_FOUND_KEY,
                 StringComparison.OrdinalIgnoreCase) ||
             string.Equals(userError.I18NKey, AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY,
                 StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return userError.ErrorCode == ErrorCode.NOT_FOUND;
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

    private static void LogSecureKeyForDebug(string context, ByteString membershipIdentifier, ReadOnlySpan<byte> secureKey)
    {
        string membershipHex = membershipIdentifier.IsEmpty
            ? string.Empty
            : Convert.ToHexString(membershipIdentifier.ToByteArray());
        string keyHex = secureKey.Length > 0 ? Convert.ToHexString(secureKey) : string.Empty;
        string keyHashHex = secureKey.Length > 0 ? Convert.ToHexString(SHA256.HashData(secureKey)) : string.Empty;

        Log.Information(
            "[OPAQUE-CLIENT-SECURE-KEY] context={Context} membership={Membership} len={Length} hex={Hex} sha256={Hash}",
            context,
            membershipHex,
            secureKey.Length,
            keyHex,
            keyHashHex);
    }

    private Result<RegistrationResult, RegistrationAttemptResult> CreateAndTrackRegistrationState(
        OpaqueClient opaqueClient,
        byte[] secureKeyCopy,
        ByteString membershipIdentifier)
    {
        RegistrationResult registrationResult = opaqueClient.CreateRegistrationRequest(secureKeyCopy);

        if (_stateManager.TryAddRegistration(membershipIdentifier, registrationResult))
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
        Log.Information("[ECLIPTIX-OPAQUE-REGISTRATION] ProcessInitializationResponse: Result={Result}, PeerOprfLength={PeerOprfLength}",
            initResponse.Result, initResponse.PeerOprf?.Length ?? 0);

        if (initResponse.Result == OpaqueOperationResult.Succeeded)
        {
            Log.Information("[ECLIPTIX-OPAQUE-REGISTRATION] Result is Succeeded, proceeding to finalization");
            return Result<Unit, RegistrationAttemptResult>.Ok(Unit.Value);
        }

        Log.Warning("[ECLIPTIX-OPAQUE-REGISTRATION] Result is NOT Succeeded: {Result}", initResponse.Result);
        registrationState.Dispose();

        string errorMessage = initResponse.Result switch
        {
            OpaqueOperationResult.InvalidCredentials =>
                localizationService[AuthenticationConstants.INVALID_CREDENTIALS_KEY],
            _ => localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]
        };

        return Result<Unit, RegistrationAttemptResult>.Err(
            CreateAttemptFailure(errorMessage, false));
    }

    private static void CleanupSensitiveRegistrationData(
        byte[]? secureKeyCopy,
        byte[]? serverRegistrationResponse,
        byte[]? registrationRecord)
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
    }

    private async Task<RegistrationAttemptResult>
        FinalizeAndCompleteRegistrationAsync(
            OpaqueClient opaqueClient,
            OpaqueRegistrationInitResponse initResponse,
            RegistrationResult trackedRegistrationResult,
            ByteString membershipIdentifier,
            uint connectId,
            RpcRequestContext requestContext,
            CancellationToken cancellationToken)
    {
        Log.Information("[ECLIPTIX-OPAQUE-REGISTRATION] FinalizeAndCompleteRegistrationAsync: PeerOprf.Length={Length}, Expected=64",
            initResponse.PeerOprf?.Length ?? 0);

        if (initResponse.PeerOprf == null || initResponse.PeerOprf.IsEmpty)
        {
            return CreateAttemptFailure(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY],
                false);
        }

        byte[] serverRegistrationResponse = new byte[initResponse.PeerOprf.Length];
        byte[]? registrationRecord = null;

        try
        {
            initResponse.PeerOprf.Span.CopyTo(serverRegistrationResponse);

            Log.Information("[ECLIPTIX-OPAQUE-REGISTRATION] Calling FinalizeRegistration with response length={Length}",
                serverRegistrationResponse.Length);

            registrationRecord = opaqueClient.FinalizeRegistration(serverRegistrationResponse, trackedRegistrationResult);

            OpaqueRegistrationCompleteRequest completeRequest = new()
            {
                PeerRegistrationRecord = ByteString.CopyFrom(registrationRecord),
                MembershipId = membershipIdentifier
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
                    allowDuplicates: true,
                    requestContext: requestContext,
                    token: cancellationToken)
                .ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return CreateAttemptFailure(
                    FormatNetworkFailure(networkResult.UnwrapErr()),
                    IsTransientRegistrationFailure(networkResult.UnwrapErr()));
            }

            OpaqueRegistrationCompleteResponse completeResponse =
                await responseSource.Task.ConfigureAwait(false);

            if (completeResponse.Result != OpaqueOperationResult.Succeeded)
            {
                return CreateAttemptFailure(
                    localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY],
                    false);
            }

            if (completeResponse.ActiveAccount?.MembershipId != null)
            {
                await applicationSecureStorageProvider
                    .SetCurrentAccountIdAsync(completeResponse.ActiveAccount.MembershipId)
                    .ConfigureAwait(false);
            }

            return CreateAttemptSuccess();
        }
        finally
        {
            CleanupSensitiveRegistrationData(null, serverRegistrationResponse, registrationRecord);
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
        const int maxIterations = 2;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool allowReinit = iteration == 0;

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

            requestContext.MarkReinitAttempted();
        }

        return CreateAttemptFailure(
            localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY],
            false);
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

        try
        {
            Result<SecureKeyPreparationResult, RegistrationAttemptResult> prepareResult =
                PrepareSecureKeyForRegistration(secureKey);

            if (prepareResult.IsErr)
            {
                return prepareResult.UnwrapErr();
            }

            secureKeyCopy = prepareResult.Unwrap().SecureKeyCopy;
            LogSecureKeyForDebug("registration", membershipIdentifier, secureKeyCopy);

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

            CleanupTrackedRegistration(membershipIdentifier);
            return attemptResult;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleRegistrationException(membershipIdentifier, ref registrationResult);
            return CreateAttemptFailure(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY],
                IsTransientException(ex));
        }
        finally
        {
            registrationResult?.Dispose();
            CleanupSensitiveRegistrationData(secureKeyCopy, null, null);
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

        return await FinalizeAndCompleteRegistrationAsync(
            opaqueClient,
            initResponse,
            registrationState,
            membershipIdentifier,
            connectId,
            requestContext,
            cancellationToken).ConfigureAwait(false);
    }

    private void CleanupTrackedRegistration(ByteString membershipIdentifier) =>
        _stateManager.CleanupRegistration(membershipIdentifier);

    private void HandleRegistrationException(ByteString membershipIdentifier,
        ref RegistrationResult? registrationResult)
    {
        _stateManager.CleanupRegistration(membershipIdentifier);
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
            PeerOprf = ByteString.CopyFrom(registrationRequest),
            MembershipId = membershipIdentifier
        };

        TaskCompletionSource<OpaqueRegistrationInitResponse> responseSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.RegistrationInit,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                OpaqueRegistrationInitResponse response =
                    Helpers.ParseFromBytes<OpaqueRegistrationInitResponse>(payload);
                responseSource.TrySetResult(response);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, allowDuplicates: true, requestContext: requestContext, token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Err(networkResult.UnwrapErr());
        }

        OpaqueRegistrationInitResponse initResponse = await responseSource.Task.ConfigureAwait(false);
        return Result<OpaqueRegistrationInitResponse, NetworkFailure>.Ok(initResponse);
    }

    private void ProcessVerificationUpdate(
        OtpCountdownUpdate verificationCountdownUpdate,
        Guid verificationIdentifier,
        uint streamConnectId,
        OtpVerificationPurpose purpose,
        Action<uint, Guid, OtpCountdownStatus, string?>? onCountdownUpdate)
    {
        _streamManager.ProcessVerificationUpdate(
            verificationIdentifier,
            streamConnectId,
            verificationCountdownUpdate.Status,
            purpose);

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
        Action<uint, Guid, OtpCountdownStatus, string?>? onCountdownUpdate,
        OtpVerificationPurpose purpose = OtpVerificationPurpose.Registration)
    {
        OtpCountdownUpdate verificationCountdownUpdate =
            Helpers.ParseFromBytes<OtpCountdownUpdate>(payload);

        if (verificationCountdownUpdate.SessionId == null ||
            verificationCountdownUpdate.SessionId.IsEmpty)
        {
            RxApp.MainThreadScheduler.Schedule(() =>
                onCountdownUpdate?.Invoke(0, Guid.Empty,
                    OtpCountdownStatus.OtpCountdownStatusFailed,
                    verificationCountdownUpdate.Message));
            return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
        }

        try
        {
            Guid verificationIdentifier = Helpers.FromByteStringToGuid(verificationCountdownUpdate.SessionId);
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

    private async Task<Result<Unit, string>> CleanupStreamAsync(Guid sessionIdentifier) =>
        await _streamManager.CloseStreamAsync(sessionIdentifier).ConfigureAwait(false);
}
