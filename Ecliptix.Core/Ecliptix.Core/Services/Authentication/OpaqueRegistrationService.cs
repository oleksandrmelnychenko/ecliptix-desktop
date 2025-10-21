using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
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
using Ecliptix.Utilities.Failures.Sodium;
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
            string errorMessage = GetNetworkFailureMessage(failure);

            if (IsVerificationSessionMissing(failure))
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
            NetworkFailure failure = result.UnwrapErr();
            string errorMessage = GetNetworkFailureMessage(failure);

            if (IsVerificationSessionMissing(failure))
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

    // README: Manual retry keeps the password in SensitiveBytes for the entire flow, recreates OPAQUE state per attempt, and zeroizes buffers when SensitiveBytes and attempt-scoped arrays are disposed.
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
            secureKey.WithSecureBytes(passwordSpan =>
            {
                createResult = SensitiveBytes.From(passwordSpan);
            });

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
            byte[]? serverRegistrationResponse = null;
            byte[]? registrationRecord = null;
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

                if (passwordCopy == null || passwordCopy.Length == 0)
                {
                    return CreateAttemptFailure(
                        localizationService[AuthenticationConstants.SecureKeyRequiredKey],
                        false,
                        requestContext);
                }

                registrationResult = opaqueClient.CreateRegistrationRequest(passwordCopy);
                RegistrationResult registrationState = registrationResult;

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

                if (initResponse.Result != OpaqueRegistrationInitResponse.Types.UpdateResult.Succeeded)
                {
                    registrationState.Dispose();
                    registrationResult = null;

                    string errorMessage = initResponse.Result switch
                    {
                        OpaqueRegistrationInitResponse.Types.UpdateResult.InvalidCredentials =>
                            localizationService[AuthenticationConstants.InvalidCredentialsKey],
                        _ => localizationService[AuthenticationConstants.RegistrationFailedKey]
                    };

                    return CreateAttemptFailure(errorMessage, false, requestContext);
                }

                if (_opaqueRegistrationState.TryAdd(membershipIdentifier, registrationState))
                {
                    trackedRegistrationResult = registrationState;
                    registrationResult = null;
                }
                else
                {
                    registrationState.Dispose();
                    registrationResult = null;
                    return CreateAttemptFailure(
                        localizationService[AuthenticationConstants.RegistrationFailedKey],
                        true,
                        requestContext);
                }

                serverRegistrationResponse = GC.AllocateUninitializedArray<byte>(initResponse.PeerOprf.Length);
                initResponse.PeerOprf.Span.CopyTo(serverRegistrationResponse);

                registrationRecord =
                    opaqueClient.FinalizeRegistration(serverRegistrationResponse, trackedRegistrationResult!);

                OpaqueRegistrationCompleteRequest completeRequest = new()
                {
                    PeerRegistrationRecord = ByteString.CopyFrom(registrationRecord),
                    MembershipIdentifier = membershipIdentifier
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
                    NetworkFailure failure = networkResult.UnwrapErr();

                    if (_opaqueRegistrationState.TryRemove(membershipIdentifier,
                            out RegistrationResult? failedResult))
                    {
                        failedResult.Dispose();
                    }

                    trackedRegistrationResult = null;

                    if (allowReinit && ShouldReinit(failure))
                    {
                        allowReinit = false;
                        requestContext.MarkReinitAttempted();
                        Serilog.Log.Information(
                            "[REGISTRATION-FLOW-REINIT] Registration complete failed due to missing flow, rerunning init. CorrelationId: {CorrelationId}, IdempotencyKey: {IdempotencyKey}",
                            requestContext.CorrelationId,
                            requestContext.IdempotencyKey);
                        continue;
                    }

                    return CreateAttemptFailure(
                        FormatNetworkFailure(failure),
                        IsTransientRegistrationFailure(failure),
                        requestContext);
                }

                OpaqueRegistrationCompleteResponse completeResponse =
                    await responseSource.Task.ConfigureAwait(false);

                if (_opaqueRegistrationState.TryRemove(membershipIdentifier,
                        out RegistrationResult? completedResult))
                {
                    completedResult.Dispose();
                }

                trackedRegistrationResult = null;

                if (completeResponse.Result != OpaqueRegistrationCompleteResponse.Types.RegistrationResult.Succeeded)
                {
                    string errorMessage = localizationService[AuthenticationConstants.RegistrationFailedKey];
                    return CreateAttemptFailure(errorMessage, false, requestContext);
                }

                return CreateAttemptSuccess(requestContext);
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
                if (registrationResult != null)
                {
                    registrationResult.Dispose();
                }

                if (trackedRegistrationResult != null)
                {
                    trackedRegistrationResult.Dispose();
                }

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

    private static RegistrationAttemptResult CreateAttemptSuccess(RpcRequestContext context) =>
        new(Result<Unit, string>.Ok(Unit.Value), false, context.CorrelationId, context.IdempotencyKey, string.Empty);

    private static RegistrationAttemptResult CreateAttemptFailure(string error, bool isTransient,
        RpcRequestContext context) =>
        new(Result<Unit, string>.Err(error), isTransient, context.CorrelationId, context.IdempotencyKey, error);

    private readonly record struct RegistrationAttemptResult(
        Result<Unit, string> Outcome,
        bool IsTransient,
        string CorrelationId,
        string IdempotencyKey,
        string FailureMessage);

    private static bool IsTransientRpcStatus(StatusCode statusCode) =>
        statusCode == StatusCode.Unavailable ||
        statusCode == StatusCode.DeadlineExceeded ||
        statusCode == StatusCode.Cancelled;

    private static bool IsTransientException(Exception exception) =>
        exception switch
        {
            RpcException rpcException when IsTransientRpcStatus(rpcException.StatusCode) => true,
            IOException => true,
            SocketException => true,
            TimeoutException => true,
            _ => false
        };

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


        OpaqueRegistrationInitRequest request = new()
        {
            PeerOprf = ByteString.CopyFrom(registrationRequest),
            MembershipIdentifier = membershipIdentifier
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

    private static string GetNetworkFailureMessage(NetworkFailure failure) =>
        failure.UserError?.Message ?? failure.Message;

    private static bool IsVerificationSessionMissing(NetworkFailure failure)
    {
        if (failure.UserError is not { } userError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(userError.I18nKey) &&
            (string.Equals(userError.I18nKey, AuthenticationConstants.SessionNotFoundKey, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(userError.I18nKey, AuthenticationConstants.VerificationSessionExpiredKey, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return userError.ErrorCode == ErrorCode.NotFound;
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
