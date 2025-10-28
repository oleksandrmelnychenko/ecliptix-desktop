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
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using Grpc.Core;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

public sealed class SignInResult(
    SodiumSecureMemoryHandle masterKeyHandle,
    Ecliptix.Protobuf.Membership.Membership? membership,
    Protobuf.Account.Account? activeAccount = null)
    : IDisposable
{
    public SodiumSecureMemoryHandle? MasterKeyHandle { get; private set; } = masterKeyHandle;
    public Protobuf.Membership.Membership? Membership { get; } = membership;
    public Protobuf.Account.Account? ActiveAccount { get; } = activeAccount;

    public void Dispose()
    {
        MasterKeyHandle?.Dispose();
        MasterKeyHandle = null;
    }
}

internal sealed class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IServerPublicKeyProvider serverPublicKeyProvider)
    : IAuthenticationService, IDisposable
{
    private const int MaxAllowedZeroBytes = 12;
    private const int MaxSignInFlowAttempts = 3;

    private static readonly Dictionary<OpaqueResult, string> OpaqueErrorMessages = new()
    {
        { OpaqueResult.InvalidInput, AuthenticationConstants.InvalidCredentialsKey },
        { OpaqueResult.CryptoError, AuthenticationConstants.CommonUnexpectedErrorKey },
        { OpaqueResult.MemoryError, AuthenticationConstants.CommonUnexpectedErrorKey },
        { OpaqueResult.ValidationError, AuthenticationConstants.InvalidCredentialsKey },
        { OpaqueResult.AuthenticationError, AuthenticationConstants.InvalidCredentialsKey },
        { OpaqueResult.InvalidPublicKey, AuthenticationConstants.CommonUnexpectedErrorKey },
    };

    private readonly Lock _opaqueClientLock = new();
    private OpaqueClient? _opaqueClient;

    public async Task<Result<Unit, AuthenticationFailure>> SignInAsync(string mobileNumber,
        SecureTextBuffer securePassword,
        uint connectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            string mobileRequiredError = localizationService[AuthenticationConstants.MobileNumberRequiredKey];
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.MobileNumberRequired(mobileRequiredError));
        }

        SensitiveBytes? passwordBytes = null;
        Result<SensitiveBytes, SodiumFailure>? createResult = null;

        try
        {
            securePassword.WithSecureBytes(passwordSpan => { createResult = SensitiveBytes.From(passwordSpan); });

            if (createResult == null || createResult.Value.IsErr)
            {
                string errorMessage = createResult?.IsErr == true
                    ? $"Failed to create secure password buffer: {createResult.Value.UnwrapErr().Message}"
                    : localizationService[AuthenticationConstants.SecureKeyRequiredKey];
                return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.PasswordRequired(errorMessage));
            }

            passwordBytes = createResult.Value.Unwrap();

            if (passwordBytes.Length != 0)
            {
                return await ExecuteSignInFlowAsync(mobileNumber, passwordBytes, connectId, cancellationToken)
                    .ConfigureAwait(false);
            }

            string requiredError = localizationService[AuthenticationConstants.SecureKeyRequiredKey];
            return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.PasswordRequired(requiredError));
        }
        finally
        {
            passwordBytes?.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_opaqueClientLock)
        {
            _opaqueClient?.Dispose();
            _opaqueClient = null;
        }
    }

    private static bool IsInvalidCredentialFailure(NetworkFailure failure)
    {
        if (failure.UserError is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(failure.UserError.I18nKey) &&
               string.Equals(failure.UserError.I18nKey, AuthenticationConstants.InvalidCredentialsKey,
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

        if (!string.IsNullOrWhiteSpace(userError.I18nKey) &&
            string.Equals(userError.I18nKey, "error.auth_flow_missing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return userError.ErrorCode == ErrorCode.DependencyUnavailable &&
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
        if (identifier.Length != CryptographicConstants.GuidByteLength)
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

        return hasNonZero && zeroCount <= MaxAllowedZeroBytes;
    }

    private async Task<Result<Unit, AuthenticationFailure>> ExecuteSignInFlowAsync(string mobileNumber,
        SensitiveBytes password,
        uint connectId, CancellationToken cancellationToken)
    {
        AuthenticationFailure? lastError = null;

        for (int attempt = 1; attempt <= MaxSignInFlowAttempts; attempt++)
        {
            RpcRequestContext requestContext = RpcRequestContext.CreateNew();
            bool allowReinit = true;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using OpaqueClient opaqueClient = new(serverPublicKeyProvider.GetServerPublicKey());

                byte[]? passwordCopy = null;
                byte[]? ke2Data = null;
                byte[]? ke3Data = null;

                try
                {
                    Result<byte[], AuthenticationFailure> passwordResult = ValidateAndCopyPassword(password);
                    if (passwordResult.IsErr)
                    {
                        lastError = passwordResult.UnwrapErr();
                        break;
                    }

                    passwordCopy = passwordResult.Unwrap();

                    using KeyExchangeResult ke1Result = opaqueClient.GenerateKE1(passwordCopy);

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
                        lastError = MapNetworkFailure(initResult.UnwrapErr());
                        break;
                    }

                    OpaqueSignInInitResponse initResponse = initResult.Unwrap();

                    Result<Unit, ValidationFailure> validationResult = ValidateInitResponse(initResponse);
                    if (validationResult.IsErr)
                    {
                        ValidationFailure validation = validationResult.UnwrapErr();
                        AuthenticationFailure authFailure = validation.FailureType switch
                        {
                            ValidationFailureType.SignInFailed => AuthenticationFailure.InvalidCredentials(validation
                                .Message),
                            ValidationFailureType.LoginAttemptExceeded =>
                                AuthenticationFailure.LoginAttemptExceeded(validation.Message),
                            _ => AuthenticationFailure.UnexpectedError(validation.Message)
                        };
                        return Result<Unit, AuthenticationFailure>.Err(authFailure);
                    }

                    ke2Data = initResponse.ServerStateToken.ToByteArray();

                    Result<byte[], AuthenticationFailure> ke3Result =
                        PerformOpaqueKE3Exchange(opaqueClient, ke2Data, ke1Result);
                    if (ke3Result.IsErr)
                    {
                        return Result<Unit, AuthenticationFailure>.Err(ke3Result.UnwrapErr());
                    }

                    ke3Data = ke3Result.Unwrap();

                    Result<SodiumSecureMemoryHandle, AuthenticationFailure> masterKeyResult =
                        ExtractMasterKeyFromOpaque(opaqueClient, ke1Result);
                    if (masterKeyResult.IsErr)
                    {
                        return Result<Unit, AuthenticationFailure>.Err(masterKeyResult.UnwrapErr());
                    }

                    using SodiumSecureMemoryHandle masterKeyHandle = masterKeyResult.Unwrap();

                    OpaqueSignInFinalizeRequest finalizeRequest = new()
                    {
                        MobileNumber = mobileNumber, ClientMac = ByteString.CopyFrom(ke3Data),
                    };

                    Result<SignInResult, NetworkFailure> finalResult =
                        await SendFinalizeRequestAndVerifyAsync(finalizeRequest, masterKeyHandle, connectId,
                            requestContext, cancellationToken).ConfigureAwait(false);

                    if (finalResult.IsOk)
                    {
                        using SignInResult signInResult = finalResult.Unwrap();

                        if (signInResult.MasterKeyHandle == null)
                        {
                            return Result<Unit, AuthenticationFailure>.Err(
                                AuthenticationFailure.SecureMemoryAllocationFailed(
                                    "Failed to create master key handle"));
                        }

                        if (signInResult.Membership != null)
                        {
                            ByteString membershipIdentifier = signInResult.Membership.UniqueIdentifier;

                            Result<SodiumSecureMemoryHandle, AuthenticationFailure> masterKeyValidationResult =
                                DeriveMasterKeyForMembership(masterKeyHandle, membershipIdentifier);

                            if (masterKeyValidationResult.IsErr)
                            {
                                return Result<Unit, AuthenticationFailure>.Err(masterKeyValidationResult.UnwrapErr());
                            }

                            using SodiumSecureMemoryHandle validatedMasterKeyHandle = masterKeyValidationResult.Unwrap();

                            Result<Unit, AuthenticationFailure> storeResult =
                                await StoreIdentityAndMembershipAsync(masterKeyHandle, signInResult)
                                    .ConfigureAwait(false);

                            if (storeResult.IsErr)
                            {
                                return Result<Unit, AuthenticationFailure>.Err(storeResult.UnwrapErr());
                            }

                            Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);
                            Result<Unit, AuthenticationFailure> protocolResult =
                                await RecreateAuthenticatedProtocolAsync(masterKeyHandle, membershipIdentifier,
                                    connectId, membershipId).ConfigureAwait(false);

                            if (protocolResult.IsErr)
                            {
                                return Result<Unit, AuthenticationFailure>.Err(protocolResult.UnwrapErr());
                            }
                        }

                        return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
                    }

                    NetworkFailure finalizeFailure = finalResult.UnwrapErr();

                    if (allowReinit && ShouldReinit(finalizeFailure))
                    {
                        allowReinit = false;
                        requestContext.MarkReinitAttempted();
                        continue;
                    }

                    lastError = MapNetworkFailure(finalizeFailure);
                    break;
                }
                finally
                {
                    SecureCleanup(passwordCopy, ke2Data, ke3Data);
                }
            }

            bool isRetryableError = lastError.FailureType == AuthenticationFailureType.NetworkRequestFailed;

            if (isRetryableError && attempt < MaxSignInFlowAttempts)
            {
                Serilog.Log.Warning(
                    "[SIGNIN-FLOW-RETRY] Server state lost, restarting sign-in flow. Attempt {Attempt}/{MaxAttempts}",
                    attempt + 1, MaxSignInFlowAttempts);

                networkProvider.ClearExhaustedOperations();
                Serilog.Log.Information("[SIGNIN-FLOW-RETRY] Cleared exhaustion state to allow retry");
                continue;
            }

            return Result<Unit, AuthenticationFailure>.Err(lastError);
        }

        return Result<Unit, AuthenticationFailure>.Err(lastError ??
                                                       AuthenticationFailure.UnexpectedError("Sign-in flow failed"));
    }

    private string GetOpaqueErrorMessage(OpaqueResult error)
    {
        return OpaqueErrorMessages.TryGetValue(error, out string? key)
            ? localizationService[key]
            : localizationService[AuthenticationConstants.CommonUnexpectedErrorKey];
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
        SodiumSecureMemoryHandle sessionKeyHandle,
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
                : localizationService[AuthenticationConstants.InvalidCredentialsKey];
            return Result<SignInResult, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(message));
        }

        SignInResult result = new(sessionKeyHandle, capturedResponse.Membership, capturedResponse.ActiveAccount);
        return Result<SignInResult, NetworkFailure>.Ok(result);
    }

    private Result<byte[], AuthenticationFailure> ValidateAndCopyPassword(SensitiveBytes password)
    {
        byte[]? passwordCopy = null;

        Result<Unit, SodiumFailure> readResult = password.WithReadAccess(span =>
        {
            passwordCopy = span.ToArray();
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);
        });

        if (readResult.IsErr)
        {
            return Result<byte[], AuthenticationFailure>.Err(
                AuthenticationFailure.UnexpectedError($"Failed to read password: {readResult.UnwrapErr().Message}"));
        }

        if (passwordCopy == null || passwordCopy.Length == 0)
        {
            string requiredError = localizationService[AuthenticationConstants.SecureKeyRequiredKey];
            return Result<byte[], AuthenticationFailure>.Err(
                AuthenticationFailure.PasswordRequired(requiredError));
        }

        return Result<byte[], AuthenticationFailure>.Ok(passwordCopy);
    }

    private Result<byte[], AuthenticationFailure> PerformOpaqueKE3Exchange(
        OpaqueClient opaqueClient,
        byte[] ke2Data,
        KeyExchangeResult ke1Result)
    {
        Result<byte[], OpaqueResult> ke3DataResult = opaqueClient.GenerateKe3(ke2Data, ke1Result);

        if (ke3DataResult.IsErr)
        {
            string errorMessage = GetOpaqueErrorMessage(ke3DataResult.UnwrapErr());
            return Result<byte[], AuthenticationFailure>.Err(
                AuthenticationFailure.InvalidCredentials(errorMessage));
        }

        return Result<byte[], AuthenticationFailure>.Ok(ke3DataResult.Unwrap());
    }

    private Result<SodiumSecureMemoryHandle, AuthenticationFailure> ExtractMasterKeyFromOpaque(
        OpaqueClient opaqueClient,
        KeyExchangeResult ke1Result)
    {
        (byte[] sessionKeyBytes, byte[] masterKeyBytes) = opaqueClient.DeriveBaseMasterKey(ke1Result);

        try
        {
            string sessionKeyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(sessionKeyBytes);
            string masterKeyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(masterKeyBytes);
            Serilog.Log.Information(
                "[CLIENT-OPAQUE-KEYS] OPAQUE keys extracted from envelope. SessionKeyFingerprint: {SessionKeyFingerprint}, MasterKeyFingerprint: {MasterKeyFingerprint}",
                sessionKeyFingerprint, masterKeyFingerprint);

            Result<SodiumSecureMemoryHandle, SodiumFailure> masterKeyHandleResult =
                SodiumSecureMemoryHandle.Allocate(masterKeyBytes.Length);
            if (masterKeyHandleResult.IsErr)
            {
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.SecureMemoryAllocationFailed(masterKeyHandleResult.UnwrapErr().Message));
            }

            SodiumSecureMemoryHandle masterKeyHandle = masterKeyHandleResult.Unwrap();
            Result<Unit, SodiumFailure> writeResult = masterKeyHandle.Write(masterKeyBytes);

            if (writeResult.IsErr)
            {
                masterKeyHandle.Dispose();
                return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                    AuthenticationFailure.SecureMemoryWriteFailed(writeResult.UnwrapErr().Message));
            }

            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(masterKeyHandle);
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
        Serilog.Log.Information(
            "[LOGIN-PROTOCOL-RECREATE] Recreating authenticated protocol with master key. MembershipId: {MembershipId}, ConnectId: {ConnectId}",
            membershipId, connectId);

        Result<Unit, NetworkFailure> recreateProtocolResult =
            await networkProvider.RecreateProtocolWithMasterKeyAsync(
                masterKeyHandle, membershipIdentifier, connectId).ConfigureAwait(false);

        if (recreateProtocolResult.IsErr)
        {
            NetworkFailure networkFailure = recreateProtocolResult.UnwrapErr();

            if (networkFailure.FailureType == NetworkFailureType.CriticalAuthenticationFailure)
            {
                Serilog.Log.Error(
                    "[LOGIN-PROTOCOL-RECREATE-CRITICAL] Critical authentication failure - server cannot derive identity keys. MembershipId: {MembershipId}, Error: {Error}",
                    membershipId, networkFailure.Message);
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.CriticalAuthenticationError(
                        $"Critical server error: {networkFailure.Message}"));
            }

            Serilog.Log.Error(
                "[LOGIN-PROTOCOL-RECREATE] Failed to recreate authenticated protocol. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, networkFailure.Message);
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed(
                    $"Failed to establish authenticated protocol: {networkFailure.Message}"));
        }

        Serilog.Log.Information(
            "[LOGIN-COMPLETE] Login flow completed successfully. MembershipId: {MembershipId}",
            membershipId);

        return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
    }

    private Result<SodiumSecureMemoryHandle, AuthenticationFailure> DeriveMasterKeyForMembership(
        SodiumSecureMemoryHandle masterKeyHandle,
        ByteString membershipIdentifier)
    {
        Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);

        Serilog.Log.Information(
            "[LOGIN-START] Starting login flow for MembershipId: {MembershipId}",
            membershipId);

        if (!ValidateMembershipIdentifier(membershipIdentifier))
        {
            Serilog.Log.Error(
                "[LOGIN-VALIDATION] Invalid membership identifier. MembershipId: {MembershipId}",
                membershipId);
            return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Err(
                AuthenticationFailure.InvalidMembershipIdentifier(
                    localizationService[AuthenticationConstants.InvalidCredentialsKey]));
        }

        Serilog.Log.Information(
            "[LOGIN-MASTERKEY-EXTRACT] Master key extracted from OPAQUE envelope. MembershipId: {MembershipId}",
            membershipId);

        Result<byte[], SodiumFailure> masterKeyBytesResult =
            masterKeyHandle.ReadBytes(masterKeyHandle.Length);
        if (masterKeyBytesResult.IsOk)
        {
            byte[] masterKeyBytesTemp = masterKeyBytesResult.Unwrap();
            string masterKeyFingerprint =
                CryptographicHelpers.ComputeSha256Fingerprint(masterKeyBytesTemp);
            Serilog.Log.Information(
                "[LOGIN-MASTERKEY-VERIFY] Master key fingerprint. MembershipId: {MembershipId}, MasterKeyFingerprint: {MasterKeyFingerprint}",
                membershipId, masterKeyFingerprint);
            CryptographicOperations.ZeroMemory(masterKeyBytesTemp);
        }

        return Result<SodiumSecureMemoryHandle, AuthenticationFailure>.Ok(masterKeyHandle);
    }

    private async Task<Result<Unit, AuthenticationFailure>> StoreIdentityAndMembershipAsync(
        SodiumSecureMemoryHandle masterKeyHandle,
        SignInResult signInResult)
    {
        ByteString membershipIdentifier = signInResult.Membership!.UniqueIdentifier;
        Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);

        Serilog.Log.Information(
            "[LOGIN-IDENTITY-STORE] Storing identity (master key). MembershipId: {MembershipId}",
            membershipId);

        Result<Unit, AuthenticationFailure> storeResult = await identityService
            .StoreIdentityAsync(masterKeyHandle, membershipId.ToString()).ConfigureAwait(false);

        if (storeResult.IsErr)
        {
            Serilog.Log.Error(
                "[LOGIN-IDENTITY-STORE-ERROR] Failed to store/verify master key. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, storeResult.UnwrapErr().Message);
            return Result<Unit, AuthenticationFailure>.Err(storeResult.UnwrapErr());
        }

        Serilog.Log.Information(
            "[LOGIN-IDENTITY-STORE] Identity stored and verified successfully. MembershipId: {MembershipId}",
            membershipId);

        Serilog.Log.Information(
            "[LOGIN-MEMBERSHIP-STORE] Storing membership data. MembershipId: {MembershipId}",
            membershipId);
        await applicationSecureStorageProvider
            .SetApplicationMembershipAsync(signInResult.Membership)
            .ConfigureAwait(false);
        Serilog.Log.Information(
            "[LOGIN-MEMBERSHIP-STORE] Membership data stored successfully. MembershipId: {MembershipId}",
            membershipId);

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

        if (accountIdToStore != null)
        {
            await applicationSecureStorageProvider
                .SetCurrentAccountIdAsync(accountIdToStore)
                .ConfigureAwait(false);
            Serilog.Log.Information(
                "[LOGIN-ACCOUNT-STORE] Active account stored successfully. MembershipId: {MembershipId}, AccountId: {AccountId}",
                membershipId, Helpers.FromByteStringToGuid(accountIdToStore));
        }

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

    private AuthenticationFailure MapNetworkFailure(NetworkFailure failure)
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
