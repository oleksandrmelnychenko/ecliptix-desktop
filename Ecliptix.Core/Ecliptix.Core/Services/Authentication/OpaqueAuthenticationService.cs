using System;
using System.Collections.Generic;
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
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using Grpc.Core;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IServerPublicKeyProvider serverPublicKeyProvider)
    : IAuthenticationService, IDisposable
{
    private const int MAX_ALLOWED_ZERO_BYTES = 12;
    private const int MAX_SIGN_IN_FLOW_ATTEMPTS = 3;

    private static readonly Dictionary<OpaqueResult, string> OpaqueErrorMessages = new()
    {
        { OpaqueResult.INVALID_INPUT, AuthenticationConstants.INVALID_CREDENTIALS_KEY },
        { OpaqueResult.CRYPTO_ERROR, AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY },
        { OpaqueResult.MEMORY_ERROR, AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY },
        { OpaqueResult.VALIDATION_ERROR, AuthenticationConstants.INVALID_CREDENTIALS_KEY },
        { OpaqueResult.AUTHENTICATION_ERROR, AuthenticationConstants.INVALID_CREDENTIALS_KEY },
        { OpaqueResult.INVALID_PUBLIC_KEY, AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY },
    };

    private readonly Lock _opaqueClientLock = new();
    private Option<OpaqueClient> _opaqueClient = Option<OpaqueClient>.None;

    private sealed record SignInFlowResult(
        SodiumSecureMemoryHandle MasterKeyHandle,
        ByteString MembershipIdentifier,
        Guid MembershipId) : IDisposable
    {
        public void Dispose()
        {
            MasterKeyHandle.Dispose();
        }
    }

    public async Task<Result<Unit, AuthenticationFailure>> SignInAsync(string mobileNumber,
        SecureTextBuffer secureKey,
        uint connectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            string mobileRequiredError = localizationService[AuthenticationConstants.MOBILE_NUMBER_REQUIRED_KEY];
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.MobileNumberRequired(mobileRequiredError));
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
                return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.SecureKeyRequired(errorMessage));
            }

            secureKeyBytes = createResult.Value.Unwrap();

            if (secureKeyBytes.Length != 0)
            {
                Result<SignInFlowResult, AuthenticationFailure> signInResult =
                    await ExecuteSignInFlowAsync(mobileNumber, secureKeyBytes, connectId, cancellationToken)
                        .ConfigureAwait(false);

                if (signInResult.IsErr)
                {
                    return Result<Unit, AuthenticationFailure>.Err(signInResult.UnwrapErr());
                }

                using SignInFlowResult flowResult = signInResult.Unwrap();
                return await RecreateAuthenticatedProtocolWithRetryAsync(flowResult, connectId, cancellationToken)
                    .ConfigureAwait(false);
            }

            string requiredError = localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY];
            return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.SecureKeyRequired(requiredError));
        }
        finally
        {
            secureKeyBytes?.Dispose();
        }
    }

    private async Task<Result<Unit, AuthenticationFailure>> RecreateAuthenticatedProtocolWithRetryAsync(
        SignInFlowResult signInFlowResult,
        uint connectId,
        CancellationToken cancellationToken)
    {
        const int MAX_PROTOCOL_RECREATE_ATTEMPTS = 3;
        AuthenticationFailure? lastError = null;

        try
        {
            for (int attempt = 1; attempt <= MAX_PROTOCOL_RECREATE_ATTEMPTS; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Result<Unit, AuthenticationFailure> protocolResult = await RecreateAuthenticatedProtocolAsync(
                    signInFlowResult.MasterKeyHandle,
                    signInFlowResult.MembershipIdentifier,
                    connectId,
                    signInFlowResult.MembershipId).ConfigureAwait(false);

                if (protocolResult.IsOk)
                {
                    return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
                }

                lastError = protocolResult.UnwrapErr();

                bool isRetryableError = lastError.FailureType == AuthenticationFailureType.NetworkRequestFailed;

                if (!isRetryableError || attempt >= MAX_PROTOCOL_RECREATE_ATTEMPTS)
                {
                    networkProvider.ExitOutage();
                    return Result<Unit, AuthenticationFailure>.Err(lastError);
                }

                Serilog.Log.Warning(
                    "[PROTOCOL-RECREATE-RETRY] Failed to recreate authenticated protocol, retrying. Attempt {Attempt}/{MAX_ATTEMPTS}, MembershipId: {MembershipId}",
                    attempt + 1, MAX_PROTOCOL_RECREATE_ATTEMPTS, signInFlowResult.MembershipId);

                networkProvider.ClearExhaustedOperations();
            }

            networkProvider.ExitOutage();
            return Result<Unit, AuthenticationFailure>.Err(lastError!);
        }
        finally
        {
            signInFlowResult.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_opaqueClientLock)
        {
            _opaqueClient.Do(client => client.Dispose());
            _opaqueClient = Option<OpaqueClient>.None;
        }
    }

    private static bool IsInvalidCredentialFailure(NetworkFailure failure)
    {
        if (failure.UserError is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(failure.UserError.I_18N_KEY) &&
               string.Equals(failure.UserError.I_18N_KEY, AuthenticationConstants.INVALID_CREDENTIALS_KEY,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldReinit(NetworkFailure failure)
    {
        if (failure.RequiresReinit)
        {
            return true;
        }

        if (failure.InnerException is RpcException rpcEx && GrpcErrorClassifier.IsAuthFlowMissing(rpcEx))
        {
            return true;
        }

        if (failure.UserError is not { } userError)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(userError.I_18N_KEY) &&
            string.Equals(userError.I_18N_KEY, "error.auth_flow_missing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return userError.ERROR_CODE == ERROR_CODE.DEPENDENCY_UNAVAILABLE &&
               failure.InnerException is RpcException { StatusCode: StatusCode.NotFound };
    }

    private static Result<Unit, ValidationFailure> ValidateInitResponse(OpaqueSignInInitResponse initResponse)
    {
        return initResponse.Result switch
        {
            OpaqueSignInInitResponse.Types.SignInResult.InvalidCredentials => Result<Unit, ValidationFailure>.Err(
                ValidationFailure.SignInFailed(initResponse.Message)),
            OpaqueSignInInitResponse.Types.SignInResult.LoginAttemptExceeded => Result<Unit, ValidationFailure>.Err(
                ValidationFailure.LoginAttemptExceeded(initResponse.Message)),
            _ when !string.IsNullOrEmpty(initResponse.Message) =>
                Result<Unit, ValidationFailure>.Err(
                    ValidationFailure.SignInFailed(initResponse.Message)),

            _ => Result<Unit, ValidationFailure>.Ok(Unit.Value)
        };
    }

    private static bool ValidateMembershipIdentifier(ByteString identifier)
    {
        if (identifier.Length != CryptographicConstants.GUID_BYTE_LENGTH)
        {
            return false;
        }

        ReadOnlySpan<byte> span = identifier.Span;

        int zeroCount = 0;
        bool hasNonZero = false;

        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == 0)
            {
                zeroCount++;
            }
            else
            {
                hasNonZero = true;
            }
        }

        return hasNonZero && zeroCount <= MAX_ALLOWED_ZERO_BYTES;
    }

    private async Task<Result<SignInFlowResult, AuthenticationFailure>> ExecuteSignInFlowAsync(string mobileNumber,
        SensitiveBytes secureKey,
        uint connectId, CancellationToken cancellationToken)
    {
        AuthenticationFailure? lastError = null;

        for (int attempt = 1; attempt <= MAX_SIGN_IN_FLOW_ATTEMPTS; attempt++)
        {
            Result<SignInFlowResult, AuthenticationFailure> attemptResult =
                await ExecuteSingleSignInAttemptAsync(mobileNumber, secureKey, connectId, cancellationToken)
                    .ConfigureAwait(false);

            if (attemptResult.IsOk)
            {
                networkProvider.ExitOutage();
                return attemptResult;
            }

            lastError = attemptResult.UnwrapErr();

            if (!ShouldRetrySignInAttempt(lastError, attempt))
            {
                networkProvider.ExitOutage();
                return Result<SignInFlowResult, AuthenticationFailure>.Err(lastError);
            }

            Serilog.Log.Warning(
                "[SIGNIN-FLOW-RETRY] Server state lost, restarting sign-in flow. Attempt {Attempt}/{MAX_ATTEMPTS}",
                attempt + 1, MAX_SIGN_IN_FLOW_ATTEMPTS);

            networkProvider.ClearExhaustedOperations();
        }

        networkProvider.ExitOutage();
        return Result<SignInFlowResult, AuthenticationFailure>.Err(lastError ??
            AuthenticationFailure.UnexpectedError("Sign-in flow failed"));
    }

    private async Task<Result<SignInFlowResult, AuthenticationFailure>> ExecuteSingleSignInAttemptAsync(
        string mobileNumber,
        SensitiveBytes secureKey,
        uint connectId,
        CancellationToken cancellationToken)
    {
        RpcRequestContext requestContext = RpcRequestContext.CreateNew();
        bool allowReinit = true;

        while (true)
        {
            Result<SignInFlowResult, AuthenticationFailure> result =
                await PerformOpaqueSignInExchangeAsync(mobileNumber, secureKey, connectId, requestContext,
                    cancellationToken).ConfigureAwait(false);

            if (result.IsOk)
            {
                return result;
            }

            AuthenticationFailure failure = result.UnwrapErr();

            if (ShouldAttemptReinit(failure, allowReinit))
            {
                allowReinit = false;
                requestContext.MarkReinitAttempted();
                continue;
            }

            return Result<SignInFlowResult, AuthenticationFailure>.Err(failure);
        }
    }

    private async Task<Result<SignInFlowResult, AuthenticationFailure>> PerformOpaqueSignInExchangeAsync(
        string mobileNumber,
        SensitiveBytes secureKey,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using OpaqueClient opaqueClient = new(serverPublicKeyProvider.GetServerPublicKey());

        byte[]? secureKeyCopy = null;
        byte[]? ke2Data = null;
        byte[]? ke3Data = null;
        SodiumSecureMemoryHandle? masterKeyHandle = null;
        bool ownershipTransferred = false;

        try
        {
            Result<byte[], AuthenticationFailure> secureKeyResult = ValidateAndCopySecureKey(secureKey);
            if (secureKeyResult.IsErr)
            {
                return Result<SignInFlowResult, AuthenticationFailure>.Err(secureKeyResult.UnwrapErr());
            }

            secureKeyCopy = secureKeyResult.Unwrap();

            using KeyExchangeResult ke1Result = opaqueClient.GenerateKE1(secureKeyCopy);

            Result<OpaqueSignInInitResponse, AuthenticationFailure> initResult =
                await PerformSignInInitPhaseAsync(mobileNumber, ke1Result, connectId, requestContext, cancellationToken)
                    .ConfigureAwait(false);

            if (initResult.IsErr)
            {
                return Result<SignInFlowResult, AuthenticationFailure>.Err(initResult.UnwrapErr());
            }

            OpaqueSignInInitResponse initResponse = initResult.Unwrap();
            ke2Data = initResponse.ServerStateToken.ToByteArray();

            Result<OpaqueExchangeData, AuthenticationFailure> exchangeResult =
                CompleteOpaqueKeyExchange(opaqueClient, ke2Data, ke1Result);

            if (exchangeResult.IsErr)
            {
                return Result<SignInFlowResult, AuthenticationFailure>.Err(exchangeResult.UnwrapErr());
            }

            OpaqueExchangeData exchangeData = exchangeResult.Unwrap();
            ke3Data = exchangeData.Ke3Data;
            masterKeyHandle = exchangeData.MasterKeyHandle;

            Result<SignInFlowResult, AuthenticationFailure> finalizeResult =
                await PerformSignInFinalizePhaseAsync(mobileNumber, ke3Data, masterKeyHandle, connectId, requestContext,
                    cancellationToken).ConfigureAwait(false);

            if (finalizeResult.IsOk)
            {
                ownershipTransferred = true;
            }

            return finalizeResult;
        }
        finally
        {
            if (masterKeyHandle != null && !ownershipTransferred)
            {
                masterKeyHandle.Dispose();
            }
            SecureCleanup(secureKeyCopy, ke2Data, ke3Data);
        }
    }

    private async Task<Result<OpaqueSignInInitResponse, AuthenticationFailure>> PerformSignInInitPhaseAsync(
        string mobileNumber,
        KeyExchangeResult ke1Result,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        OpaqueSignInInitRequest initRequest = new()
        {
            MobileNumber = mobileNumber,
            PeerOprf = ByteString.CopyFrom(ke1Result.GetKeyExchangeDataCopy()),
        };

        Result<OpaqueSignInInitResponse, NetworkFailure> initResult =
            await SendInitRequestAsync(initRequest, connectId, requestContext, cancellationToken)
                .ConfigureAwait(false);

        if (initResult.IsErr)
        {
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                MapNetworkFailure(initResult.UnwrapErr()));
        }

        OpaqueSignInInitResponse initResponse = initResult.Unwrap();

        Result<Unit, ValidationFailure> validationResult = ValidateInitResponse(initResponse);
        if (validationResult.IsErr)
        {
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                MapValidationFailure(validationResult.UnwrapErr()));
        }

        return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Ok(initResponse);
    }

    private Result<OpaqueExchangeData, AuthenticationFailure> CompleteOpaqueKeyExchange(
        OpaqueClient opaqueClient,
        byte[] ke2Data,
        KeyExchangeResult ke1Result)
    {
        Result<byte[], AuthenticationFailure> ke3Result = PerformOpaqueKe3Exchange(opaqueClient, ke2Data, ke1Result);
        if (ke3Result.IsErr)
        {
            return Result<OpaqueExchangeData, AuthenticationFailure>.Err(ke3Result.UnwrapErr());
        }

        byte[] ke3Data = ke3Result.Unwrap();

        Result<SodiumSecureMemoryHandle, AuthenticationFailure> masterKeyResult =
            ExtractMasterKeyFromOpaque(opaqueClient, ke1Result);

        if (masterKeyResult.IsErr)
        {
            return Result<OpaqueExchangeData, AuthenticationFailure>.Err(masterKeyResult.UnwrapErr());
        }

        SodiumSecureMemoryHandle masterKeyHandle = masterKeyResult.Unwrap();

        return Result<OpaqueExchangeData, AuthenticationFailure>.Ok(
            new OpaqueExchangeData(ke3Data, masterKeyHandle));
    }

    private async Task<Result<SignInFlowResult, AuthenticationFailure>> PerformSignInFinalizePhaseAsync(
        string mobileNumber,
        byte[] ke3Data,
        SodiumSecureMemoryHandle masterKeyHandle,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        OpaqueSignInFinalizeRequest finalizeRequest = new()
        {
            MobileNumber = mobileNumber,
            ClientMac = ByteString.CopyFrom(ke3Data),
        };

        Result<SignInResult, NetworkFailure> finalResult =
            await SendFinalizeRequestAndVerifyAsync(finalizeRequest, connectId, requestContext, cancellationToken)
                .ConfigureAwait(false);

        if (finalResult.IsErr)
        {
            return Result<SignInFlowResult, AuthenticationFailure>.Err(
                MapNetworkFailure(finalResult.UnwrapErr()));
        }

        SignInResult signInResult = finalResult.Unwrap();

        return await ProcessSignInResultAsync(masterKeyHandle, signInResult).ConfigureAwait(false);
    }

    private async Task<Result<SignInFlowResult, AuthenticationFailure>> ProcessSignInResultAsync(
        SodiumSecureMemoryHandle masterKeyHandle,
        SignInResult signInResult)
    {
        if (signInResult.Membership == null)
        {
            return Result<SignInFlowResult, AuthenticationFailure>.Ok(
                new SignInFlowResult(masterKeyHandle, ByteString.Empty, Guid.Empty));
        }

        ByteString membershipIdentifier = signInResult.Membership.UniqueIdentifier;

        Result<SodiumSecureMemoryHandle, AuthenticationFailure> masterKeyValidationResult =
            DeriveMasterKeyForMembership(masterKeyHandle, membershipIdentifier);

        if (masterKeyValidationResult.IsErr)
        {
            return Result<SignInFlowResult, AuthenticationFailure>.Err(masterKeyValidationResult.UnwrapErr());
        }

        Result<Unit, AuthenticationFailure> storeResult =
            await StoreIdentityAndMembershipAsync(masterKeyHandle, signInResult).ConfigureAwait(false);

        if (storeResult.IsErr)
        {
            return Result<SignInFlowResult, AuthenticationFailure>.Err(storeResult.UnwrapErr());
        }

        Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);
        SignInFlowResult flowResult = new(masterKeyHandle, membershipIdentifier, membershipId);

        return Result<SignInFlowResult, AuthenticationFailure>.Ok(flowResult);
    }

    private bool ShouldRetrySignInAttempt(AuthenticationFailure failure, int attempt)
    {
        bool isRetryableError = failure.FailureType == AuthenticationFailureType.NetworkRequestFailed;
        return isRetryableError && attempt < MAX_SIGN_IN_FLOW_ATTEMPTS;
    }

    private bool ShouldAttemptReinit(AuthenticationFailure failure, bool allowReinit)
    {
        if (!allowReinit)
        {
            return false;
        }

        return failure.FailureType == AuthenticationFailureType.NetworkRequestFailed;
    }

    private AuthenticationFailure MapValidationFailure(ValidationFailure validation)
    {
        return validation.FailureType switch
        {
            ValidationFailureType.SignInFailed => AuthenticationFailure.InvalidCredentials(validation.Message),
            ValidationFailureType.LoginAttemptExceeded => AuthenticationFailure.LoginAttemptExceeded(validation.Message),
            _ => AuthenticationFailure.UnexpectedError(validation.Message)
        };
    }

    private readonly record struct OpaqueExchangeData(byte[] Ke3Data, SodiumSecureMemoryHandle MasterKeyHandle);

    private string GetOpaqueErrorMessage(OpaqueResult error)
    {
        return OpaqueErrorMessages.TryGetValue(error, out string? key)
            ? localizationService[key]
            : localizationService[AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY];
    }

    private async Task<Result<OpaqueSignInInitResponse, NetworkFailure>> SendInitRequestAsync(
        OpaqueSignInInitRequest initRequest,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<OpaqueSignInInitResponse> responseCompletionSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.SignInInitRequest,
            SecureByteStringInterop.WithByteStringAsSpan(initRequest.ToByteString(), span => span.ToArray()),
            initResponsePayload =>
            {
                OpaqueSignInInitResponse response =
                    Helpers.ParseFromBytes<OpaqueSignInInitResponse>(initResponsePayload);
                responseCompletionSource.TrySetResult(response);
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, cancellationToken, waitForRecovery: false, requestContext
        ).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<OpaqueSignInInitResponse, NetworkFailure>.Err(networkResult.UnwrapErr());
        }

        OpaqueSignInInitResponse response = await responseCompletionSource.Task.ConfigureAwait(false);
        return Result<OpaqueSignInInitResponse, NetworkFailure>.Ok(response);
    }

    private async Task<Result<SignInResult, NetworkFailure>> SendFinalizeRequestAndVerifyAsync(
        OpaqueSignInFinalizeRequest finalizeRequest,
        uint connectId,
        RpcRequestContext requestContext,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<OpaqueSignInFinalizeResponse> responseCompletionSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.SignInCompleteRequest,
            SecureByteStringInterop.WithByteStringAsSpan(finalizeRequest.ToByteString(), span => span.ToArray()),
            finalizeResponsePayload =>
            {
                OpaqueSignInFinalizeResponse response =
                    Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(finalizeResponsePayload);
                responseCompletionSource.TrySetResult(response);

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, cancellationToken, waitForRecovery: false, requestContext
        ).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<SignInResult, NetworkFailure>.Err(networkResult.UnwrapErr());
        }

        OpaqueSignInFinalizeResponse capturedResponse = await responseCompletionSource.Task.ConfigureAwait(false);

        if (capturedResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
        {
            string message = capturedResponse.HasMessage
                ? capturedResponse.Message
                : localizationService[AuthenticationConstants.INVALID_CREDENTIALS_KEY];
            return Result<SignInResult, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(message));
        }

        SignInResult result = new(capturedResponse.Membership, capturedResponse.ActiveAccount);
        return Result<SignInResult, NetworkFailure>.Ok(result);
    }

    private Result<byte[], AuthenticationFailure> ValidateAndCopySecureKey(SensitiveBytes secureKey)
    {
        byte[]? secureKeyCopy = null;

        Result<Unit, SodiumFailure> readResult = secureKey.WithReadAccess(span =>
        {
            secureKeyCopy = span.ToArray();
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);
        });

        if (readResult.IsErr)
        {
            return Result<byte[], AuthenticationFailure>.Err(
                AuthenticationFailure.UnexpectedError($"Failed to read secure key: {readResult.UnwrapErr().Message}"));
        }

        if (secureKeyCopy != null && secureKeyCopy.Length != 0)
        {
            return Result<byte[], AuthenticationFailure>.Ok(secureKeyCopy);
        }

        string requiredError = localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY];
        return Result<byte[], AuthenticationFailure>.Err(
            AuthenticationFailure.SecureKeyRequired(requiredError));
    }

    private Result<byte[], AuthenticationFailure> PerformOpaqueKe3Exchange(
        OpaqueClient opaqueClient,
        byte[] ke2Data,
        KeyExchangeResult ke1Result)
    {
        Result<byte[], OpaqueResult> ke3DataResult = opaqueClient.GenerateKe3(ke2Data, ke1Result);

        if (!ke3DataResult.IsErr)
        {
            return Result<byte[], AuthenticationFailure>.Ok(ke3DataResult.Unwrap());
        }

        string errorMessage = GetOpaqueErrorMessage(ke3DataResult.UnwrapErr());
        return Result<byte[], AuthenticationFailure>.Err(
            AuthenticationFailure.InvalidCredentials(errorMessage));
    }

    private static Result<SodiumSecureMemoryHandle, AuthenticationFailure> ExtractMasterKeyFromOpaque(
        OpaqueClient opaqueClient,
        KeyExchangeResult ke1Result)
    {
        (byte[] sessionKeyBytes, byte[] masterKeyBytes) = opaqueClient.DeriveBaseMasterKey(ke1Result);

        try
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> masterKeyHandleResult =
                SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
            if (masterKeyHandleResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.SECURE_MEMORY_ALLOCATION_FAILED(masterKeyHandleResult.UnwrapErr().Message));
            }

            SodiumSecureMemoryHandle masterKeyHandle = masterKeyHandleResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = masterKeyHandle.Write(masterKeyBytes);

            if (!writeResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(masterKeyHandle);
            }

            masterKeyHandle.Dispose();
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.SECURE_MEMORY_WRITE_FAILED(writeResult.UnwrapErr().Message));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKeyBytes);
            CryptographicOperations.ZeroMemory(masterKeyBytes);
        }
    }

    private async Task<Result<Unit, AuthenticationFailure>> RecreateAuthenticatedProtocolAsync(
        SodiumSecureMemoryHandle masterKeyHandle,
        ByteString membershipIdentifier,
        uint connectId,
        Guid membershipId)
    {
        Result<Unit, NetworkFailure> recreateProtocolResult =
            await networkProvider.RecreateProtocolWithMasterKeyAsync(
                masterKeyHandle, membershipIdentifier, connectId).ConfigureAwait(false);

        if (recreateProtocolResult.IsErr)
        {
            NetworkFailure networkFailure = recreateProtocolResult.UnwrapErr();

            if (networkFailure.FailureType == NetworkFailureType.CriticalAuthenticationFailure)
            {
                Serilog.Log.Error(
                    "[LOGIN-PROTOCOL-RECREATE-CRITICAL] Critical authentication failure - server cannot derive identity keys. MembershipId: {MembershipId}, ERROR: {ERROR}",
                    membershipId, networkFailure.Message);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.CriticalAuthenticationError(
                        $"Critical server error: {networkFailure.Message}"));
            }

            Serilog.Log.Error(
                "[LOGIN-PROTOCOL-RECREATE] Failed to recreate authenticated protocol. MembershipId: {MembershipId}, ERROR: {ERROR}",
                membershipId, networkFailure.Message);
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed(
                    $"Failed to establish authenticated protocol: {networkFailure.Message}"));
        }

        return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
    }

    private Result<SodiumSecureMemoryHandle, AuthenticationFailure> DeriveMasterKeyForMembership(
        SodiumSecureMemoryHandle masterKeyHandle,
        ByteString membershipIdentifier)
    {
        Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);

        if (!ValidateMembershipIdentifier(membershipIdentifier))
        {
            Serilog.Log.Error(
                "[LOGIN-VALIDATION] Invalid membership identifier. MembershipId: {MembershipId}",
                membershipId);
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.InvalidMembershipIdentifier(
                    localizationService[AuthenticationConstants.INVALID_CREDENTIALS_KEY]));
        }

        Result<byte[], SodiumFailure> masterKeyBytesResult =
            masterKeyHandle.ReadBytes(masterKeyHandle.Length);
        if (!masterKeyBytesResult.IsOk)
        {
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(masterKeyHandle);
        }

        byte[] masterKeyBytesTemp = masterKeyBytesResult.Unwrap();
        CryptographicOperations.ZeroMemory(masterKeyBytesTemp);

        return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(masterKeyHandle);
    }

    private async Task<Result<Unit, AuthenticationFailure>> StoreIdentityAndMembershipAsync(
        SodiumSecureMemoryHandle masterKeyHandle,
        SignInResult signInResult)
    {
        ByteString membershipIdentifier = signInResult.Membership!.UniqueIdentifier;
        Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);

        Result<Unit, AuthenticationFailure> storeResult = await identityService
            .StoreIdentityAsync(masterKeyHandle, membershipId.ToString()).ConfigureAwait(false);

        if (storeResult.IsErr)
        {
            Serilog.Log.Error(
                "[LOGIN-IDENTITY-STORE-ERROR] Failed to store/verify master key. MembershipId: {MembershipId}, ERROR: {ERROR}",
                membershipId, storeResult.UnwrapErr().Message);
            return Result<Unit, AuthenticationFailure>.Err(storeResult.UnwrapErr());
        }

        await applicationSecureStorageProvider
            .SetApplicationMembershipAsync(signInResult.Membership)
            .ConfigureAwait(false);

        ByteString? accountIdToStore = null;
        if (signInResult.ActiveAccount != null && signInResult.ActiveAccount.UniqueIdentifier != null)
        {
            accountIdToStore = signInResult.ActiveAccount.UniqueIdentifier;
        }
        else if (signInResult.Membership?.AccountUniqueIdentifier != null &&
                 signInResult.Membership.AccountUniqueIdentifier.Length > 0)
        {
            accountIdToStore = signInResult.Membership.AccountUniqueIdentifier;
        }

        if (accountIdToStore == null)
        {
            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }

        await applicationSecureStorageProvider
            .SetCurrentAccountIdAsync(accountIdToStore)
            .ConfigureAwait(false);

        return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
    }

    private static void SecureCleanup(params byte[]?[] buffers)
    {
        foreach (byte[]? buffer in buffers)
        {
            if (buffer is { Length: > 0 })
            {
                CryptographicOperations.ZeroMemory(buffer);
            }
        }
    }

    private static AuthenticationFailure MapNetworkFailure(NetworkFailure failure)
    {
        string message = failure.UserError?.Message ?? failure.Message;

        return failure.FailureType switch
        {
            NetworkFailureType.CriticalAuthenticationFailure =>
                AuthenticationFailure.CriticalAuthenticationError(message),
            NetworkFailureType.InvalidRequestType when IsInvalidCredentialFailure(failure) =>
                AuthenticationFailure.InvalidCredentials(message),
            _ => AuthenticationFailure.NetworkRequestFailed(message)
        };
    }
}
