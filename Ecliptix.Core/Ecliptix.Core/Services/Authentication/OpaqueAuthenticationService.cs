using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.KeySplitting;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.Authentication;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

public sealed class SignInResult(
    SodiumSecureMemoryHandle masterKeyHandle,
    Ecliptix.Protobuf.Membership.Membership? membership)
    : IDisposable
{
    public SodiumSecureMemoryHandle? MasterKeyHandle { get; private set; } = masterKeyHandle;
    public Protobuf.Membership.Membership? Membership { get; } = membership;

    public void Dispose()
    {
        MasterKeyHandle?.Dispose();
        MasterKeyHandle = null;
    }
}

public sealed class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEventService systemEvents,
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IHardenedKeyDerivation hardenedKeyDerivation,
    IServerPublicKeyProvider serverPublicKeyProvider)
    : IAuthenticationService, IDisposable
{
    private const int MaxAllowedZeroBytes = 12;

    private readonly Lock _opaqueClientLock = new();
    private OpaqueClient? _opaqueClient;
    private byte[]? _cachedServerPublicKey;

    private static readonly Dictionary<OpaqueResult, string> OpaqueErrorMessages = new()
    {
        { OpaqueResult.InvalidInput, AuthenticationConstants.InvalidCredentialsKey },
        { OpaqueResult.CryptoError, AuthenticationConstants.CommonUnexpectedErrorKey },
        { OpaqueResult.MemoryError, AuthenticationConstants.CommonUnexpectedErrorKey },
        { OpaqueResult.ValidationError, AuthenticationConstants.InvalidCredentialsKey},
        { OpaqueResult.AuthenticationError, AuthenticationConstants.InvalidCredentialsKey },
        { OpaqueResult.InvalidPublicKey, AuthenticationConstants.CommonUnexpectedErrorKey },
    };

    private static readonly KeyDerivationOptions DefaultKeyDerivationOptions = new()
    {
        MemorySize = CryptographicConstants.Argon2.DefaultMemorySize,
        Iterations = CryptographicConstants.Argon2.DefaultIterations,
        DegreeOfParallelism = CryptographicConstants.Argon2.DefaultParallelism,
        UseHardwareEntropy = false,
        OutputLength = CryptographicConstants.Argon2.DefaultOutputLength
    };

    private string GetOpaqueErrorMessage(OpaqueResult error)
    {
        return OpaqueErrorMessages.TryGetValue(error, out string? key)
            ? localizationService[key]
            : localizationService[AuthenticationConstants.CommonUnexpectedErrorKey];
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

    public async Task<Result<Unit, AuthenticationFailure>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword,
        uint connectId)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            string mobileRequiredError = localizationService[AuthenticationConstants.MobileNumberRequiredKey];
            return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.MobileNumberRequired(mobileRequiredError));
        }

        byte[]? passwordBytes = null;
        try
        {
            securePassword.WithSecureBytes(bytes => passwordBytes = bytes.ToArray());

            if (passwordBytes != null && passwordBytes.Length != 0)
            {
                return await ExecuteSignInFlowAsync(mobileNumber, passwordBytes, connectId).ConfigureAwait(false);
            }

            string requiredError = localizationService[AuthenticationConstants.SecureKeyRequiredKey];
            return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.PasswordRequired(requiredError));
        }
        catch (Exception ex)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.UnexpectedError(localizationService[AuthenticationConstants.CommonUnexpectedErrorKey], ex));
        }
        finally
        {
            if (passwordBytes != null)
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
    }

    private async Task<Result<Unit, AuthenticationFailure>> ExecuteSignInFlowAsync(string mobileNumber, byte[] passwordBytes,
        uint connectId)
    {
        OpaqueClient opaqueClient = GetOrCreateOpaqueClient();

        using KeyExchangeResult ke1Result = opaqueClient.GenerateKE1(passwordBytes);

        OpaqueSignInInitRequest initRequest = new()
        {
            MobileNumber = mobileNumber,
            PeerOprf = ByteString.CopyFrom(ke1Result.KeyExchangeData),
        };

        Result<OpaqueSignInInitResponse, AuthenticationFailure> initResult = await SendInitRequestAsync(initRequest, connectId).ConfigureAwait(false);
        if (initResult.IsErr)
        {
            return Result<Unit, AuthenticationFailure>.Err(initResult.UnwrapErr());
        }

        OpaqueSignInInitResponse initResponse = initResult.Unwrap();

        Result<Unit, ValidationFailure> validationResult = ValidateInitResponse(initResponse);
        if (validationResult.IsErr)
        {
            ValidationFailure validation = validationResult.UnwrapErr();
            AuthenticationFailure authFailure = validation.FailureType switch
            {
                ValidationFailureType.SignInFailed => AuthenticationFailure.InvalidCredentials(validation.Message),
                ValidationFailureType.LoginAttemptExceeded => AuthenticationFailure.LoginAttemptExceeded(validation.Message),
                _ => AuthenticationFailure.UnexpectedError(validation.Message)
            };
            return Result<Unit, AuthenticationFailure>.Err(authFailure);
        }

        byte[] ke2Data = initResponse.ServerStateToken.ToByteArray();

        Result<byte[], OpaqueResult> ke3DataResult = opaqueClient.GenerateKe3(ke2Data, ke1Result);

        if (ke3DataResult.IsErr)
        {
            string errorMessage = GetOpaqueErrorMessage(ke3DataResult.UnwrapErr());
            return Result<Unit, AuthenticationFailure>.Err(AuthenticationFailure.InvalidCredentials(errorMessage));
        }

        byte[] ke3Data = ke3DataResult.Unwrap();
        byte[] baseSessionKeyBytes = opaqueClient.DeriveBaseMasterKey(ke1Result);

        string baseKeyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(baseSessionKeyBytes);
        Serilog.Log.Information("[CLIENT-OPAQUE-EXPORTKEY] OPAQUE export_key (base session key) derived. BaseKeyFingerprint: {BaseKeyFingerprint}", baseKeyFingerprint);

        Result<SodiumSecureMemoryHandle, SodiumFailure> baseHandleResult =
            SodiumSecureMemoryHandle.Allocate(baseSessionKeyBytes.Length);
        if (baseHandleResult.IsErr)
        {
            CryptographicOperations.ZeroMemory(baseSessionKeyBytes);
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.SecureMemoryAllocationFailed(baseHandleResult.UnwrapErr().Message));
        }

        using SodiumSecureMemoryHandle baseKeyHandle = baseHandleResult.Unwrap();
        Result<Unit, SodiumFailure> writeResult = baseKeyHandle.Write(baseSessionKeyBytes);
        CryptographicOperations.ZeroMemory(baseSessionKeyBytes);

        if (writeResult.IsErr)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.SecureMemoryWriteFailed(writeResult.UnwrapErr().Message));
        }

        Result<SodiumSecureMemoryHandle, KeySplittingFailure> enhancedMasterKeyHandleResult =
            await hardenedKeyDerivation.DeriveEnhancedMasterKeyHandleAsync(
                baseKeyHandle,
                StorageKeyConstants.SessionContext.SignInSession,
                DefaultKeyDerivationOptions).ConfigureAwait(false);

        if (enhancedMasterKeyHandleResult.IsErr)
        {
            return Result<Unit, AuthenticationFailure>.Err(
                AuthenticationFailure.KeyDerivationFailed(enhancedMasterKeyHandleResult.UnwrapErr().Message));
        }

        using SodiumSecureMemoryHandle enhancedMasterKeyHandle = enhancedMasterKeyHandleResult.Unwrap();

        OpaqueSignInFinalizeRequest finalizeRequest = new()
        {
            MobileNumber = mobileNumber,
            ClientMac = ByteString.CopyFrom(ke3Data),
        };

        Result<SignInResult, AuthenticationFailure> finalResult =
            await SendFinalizeRequestAndVerifyAsync(finalizeRequest, enhancedMasterKeyHandle, connectId).ConfigureAwait(false);

        if (finalResult.IsOk)
        {
            using SignInResult signInResult = finalResult.Unwrap();

            if (signInResult.MasterKeyHandle == null)
            {
                return Result<Unit, AuthenticationFailure>.Err(
                    AuthenticationFailure.SecureMemoryAllocationFailed("Failed to create master key handle"));
            }

            if (signInResult.Membership != null)
            {
                ByteString membershipIdentifier = signInResult.Membership.UniqueIdentifier;
                Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);

                Serilog.Log.Information("[LOGIN-START] Starting login flow for MembershipId: {MembershipId}", membershipId);

                if (!ValidateMembershipIdentifier(membershipIdentifier))
                {
                    Serilog.Log.Error("[LOGIN-VALIDATION] Invalid membership identifier. MembershipId: {MembershipId}", membershipId);
                    await systemEvents.NotifySystemStateAsync(SystemState.FatalError, "Invalid membership identifier").ConfigureAwait(false);
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.InvalidMembershipIdentifier(localizationService[AuthenticationConstants.InvalidCredentialsKey]));
                }

                Serilog.Log.Information("[LOGIN-MASTERKEY-DERIVE] Deriving master key from enhanced key. MembershipId: {MembershipId}", membershipId);

                Result<SodiumSecureMemoryHandle, SodiumFailure> masterKeyHandleResult =
                    MasterKeyDerivation.DeriveMasterKeyHandle(enhancedMasterKeyHandle, membershipIdentifier);

                if (masterKeyHandleResult.IsErr)
                {
                    Serilog.Log.Error("[LOGIN-MASTERKEY-DERIVE] Master key derivation failed. MembershipId: {MembershipId}, Error: {Error}",
                        membershipId, masterKeyHandleResult.UnwrapErr().Message);
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.MasterKeyDerivationFailed(masterKeyHandleResult.UnwrapErr().Message));
                }

                Serilog.Log.Information("[LOGIN-MASTERKEY-DERIVE] Master key derived successfully. MembershipId: {MembershipId}", membershipId);

                using SodiumSecureMemoryHandle masterKeyHandle = masterKeyHandleResult.Unwrap();

                Result<byte[], SodiumFailure> masterKeyBytesResult = masterKeyHandle.ReadBytes(masterKeyHandle.Length);
                if (masterKeyBytesResult.IsOk)
                {
                    byte[] masterKeyBytesTemp = masterKeyBytesResult.Unwrap();
                    string masterKeyFingerprint = CryptographicHelpers.ComputeSha256Fingerprint(masterKeyBytesTemp);
                    Serilog.Log.Information("[LOGIN-MASTERKEY-VERIFY] Master key fingerprint. MembershipId: {MembershipId}, MasterKeyFingerprint: {MasterKeyFingerprint}",
                        membershipId, masterKeyFingerprint);
                    CryptographicOperations.ZeroMemory(masterKeyBytesTemp);
                }

                Serilog.Log.Information("[LOGIN-IDENTITY-STORE] Storing identity (master key). MembershipId: {MembershipId}", membershipId);
                Result<Unit, AuthenticationFailure> storeResult = await identityService.StoreIdentityAsync(masterKeyHandle, membershipId.ToString()).ConfigureAwait(false);

                if (storeResult.IsErr)
                {
                    Serilog.Log.Error("[LOGIN-IDENTITY-STORE-ERROR] Failed to store/verify master key. MembershipId: {MembershipId}, Error: {Error}",
                        membershipId, storeResult.UnwrapErr().Message);
                    return Result<Unit, AuthenticationFailure>.Err(storeResult.UnwrapErr());
                }

                Serilog.Log.Information("[LOGIN-IDENTITY-STORE] Identity stored and verified successfully. MembershipId: {MembershipId}", membershipId);

                Serilog.Log.Information("[LOGIN-MEMBERSHIP-STORE] Storing membership data. MembershipId: {MembershipId}", membershipId);
                await applicationSecureStorageProvider.SetApplicationMembershipAsync(signInResult.Membership).ConfigureAwait(false);
                Serilog.Log.Information("[LOGIN-MEMBERSHIP-STORE] Membership data stored successfully. MembershipId: {MembershipId}", membershipId);

                Serilog.Log.Information("[LOGIN-PROTOCOL-RECREATE] Recreating authenticated protocol with master key. MembershipId: {MembershipId}, ConnectId: {ConnectId}",
                    membershipId, connectId);

                Result<Unit, NetworkFailure> recreateProtocolResult =
                    await networkProvider.RecreateProtocolWithMasterKeyAsync(
                        masterKeyHandle, membershipIdentifier, connectId).ConfigureAwait(false);

                if (recreateProtocolResult.IsErr)
                {
                    Serilog.Log.Error("[LOGIN-PROTOCOL-RECREATE] Failed to recreate authenticated protocol. MembershipId: {MembershipId}, Error: {Error}",
                        membershipId, recreateProtocolResult.UnwrapErr().Message);
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.NetworkRequestFailed(
                            $"Failed to establish authenticated protocol: {recreateProtocolResult.UnwrapErr().Message}"));
                }

                Serilog.Log.Information("[LOGIN-COMPLETE] Login flow completed successfully. MembershipId: {MembershipId}", membershipId);
            }

            return Result<Unit, AuthenticationFailure>.Ok(Unit.Value);
        }

        return Result<Unit, AuthenticationFailure>.Err(finalResult.UnwrapErr());
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

    private async Task<Result<OpaqueSignInInitResponse, AuthenticationFailure>> SendInitRequestAsync(
        OpaqueSignInInitRequest initRequest, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInInitResponse> responseCompletionSource = new();

        using CancellationTokenSource timeoutCts = new(NetworkTimeoutConstants.DefaultRequestTimeoutMs);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInInitRequest,
            SecureByteStringInterop.WithByteStringAsSpan(initRequest.ToByteString(), span => span.ToArray()),
            initResponsePayload =>
            {
                try
                {
                    OpaqueSignInInitResponse response =
                        Helpers.ParseFromBytes<OpaqueSignInInitResponse>(initResponsePayload);
                    responseCompletionSource.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[OPAQUE-SIGNIN-INIT] Failed to parse sign-in initialization response");
                    responseCompletionSource.TrySetException(ex);
                }
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, timeoutCts.Token, waitForRecovery: false
        ).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed(networkResult.UnwrapErr().Message));
        }

        try
        {
            OpaqueSignInInitResponse response = await responseCompletionSource.Task.ConfigureAwait(false);
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Ok(response);
        }
        catch (OperationCanceledException)
        {
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed($"Sign-in initialization request timed out after {NetworkTimeoutConstants.DefaultRequestTimeoutMs / 1000} seconds"));
        }
    }

    private async Task<Result<SignInResult, AuthenticationFailure>> SendFinalizeRequestAndVerifyAsync(
        OpaqueSignInFinalizeRequest finalizeRequest,
        SodiumSecureMemoryHandle sessionKeyHandle, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInFinalizeResponse> responseCompletionSource = new();

        using CancellationTokenSource timeoutCts = new(NetworkTimeoutConstants.DefaultRequestTimeoutMs);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInCompleteRequest,
            SecureByteStringInterop.WithByteStringAsSpan(finalizeRequest.ToByteString(), span => span.ToArray()),
            finalizeResponsePayload =>
            {
                try
                {
                    OpaqueSignInFinalizeResponse response =
                        Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(finalizeResponsePayload);
                    responseCompletionSource.TrySetResult(response);
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[OPAQUE-SIGNIN-FINALIZE] Failed to parse sign-in finalization response");
                    responseCompletionSource.TrySetException(ex);
                }
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, timeoutCts.Token, waitForRecovery: false
        ).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<SignInResult, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed(networkResult.UnwrapErr().Message));
        }

        OpaqueSignInFinalizeResponse capturedResponse;
        try
        {
            capturedResponse = await responseCompletionSource.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Result<SignInResult, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed($"Sign-in finalization request timed out after {NetworkTimeoutConstants.DefaultRequestTimeoutMs / 1000} seconds"));
        }

        if (capturedResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
        {
            string message = capturedResponse.HasMessage
                ? capturedResponse.Message
                : localizationService[AuthenticationConstants.InvalidCredentialsKey];
            return Result<SignInResult, AuthenticationFailure>.Err(AuthenticationFailure.InvalidCredentials(message));
        }

        SignInResult result = new(sessionKeyHandle, capturedResponse.Membership);
        return Result<SignInResult, AuthenticationFailure>.Ok(result);
    }

    private static bool ValidateMembershipIdentifier(ByteString identifier)
    {
        if (identifier.Length != CryptographicConstants.GuidByteLength)
            return false;

        ReadOnlySpan<byte> span = identifier.Span;

        int zeroCount = 0;
        bool hasNonZero = false;

        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == 0)
                zeroCount++;
            else
                hasNonZero = true;
        }

        return hasNonZero && zeroCount <= MaxAllowedZeroBytes;
    }

    public void Dispose()
    {
        lock (_opaqueClientLock)
        {
            _opaqueClient?.Dispose();
            _opaqueClient = null;
        }
    }
}
