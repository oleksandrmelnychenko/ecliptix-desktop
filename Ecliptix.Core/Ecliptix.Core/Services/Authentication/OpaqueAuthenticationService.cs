using System;
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
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protocol.System.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using Serilog;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

public record SignInResult(byte[] SessionKey, Ecliptix.Protobuf.Membership.Membership? Membership);

public class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEventService systemEvents,
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISessionKeyService sessionKeyService,
    IEnhancedKeyDerivation enhancedKeyDerivation,
    IMultiLocationKeyStorage multiLocationKeyStorage,
    ISecureKeySplitter keySplitter,
    IShareAuthenticationService shareAuthenticationService)
    : IAuthenticationService, IDisposable
{
    private readonly Lock _opaqueClientLock = new();
    private OpaqueClient? _opaqueClient;
    private byte[]? _cachedServerPublicKey;

    private byte[] ServerPublicKey() =>
        SecureByteStringInterop.WithByteStringAsSpan(
            networkProvider.ApplicationInstanceSettings.ServerPublicKey,
            span => span.ToArray());

    private OpaqueClient GetOrCreateOpaqueClient(byte[] serverPublicKey)
    {
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

    public async Task<Result<Unit, string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword,
        uint connectId)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            string mobileRequiredError = localizationService[AuthenticationConstants.MobileNumberRequiredKey];
            return Result<Unit, string>.Err(mobileRequiredError);
        }

        byte[]? passwordBytes = null;
        try
        {
            securePassword.WithSecureBytes(bytes => passwordBytes = bytes.ToArray());

            if (passwordBytes != null && passwordBytes.Length != 0)
            {
                return await ExecuteSignInFlowAsync(mobileNumber, passwordBytes, connectId);
            }

            string requiredError = localizationService[AuthenticationConstants.SecureKeyRequiredKey];
            return Result<Unit, string>.Err(requiredError);
        }
        catch (Exception)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.CommonUnexpectedErrorKey]);
        }
        finally
        {
            passwordBytes?.AsSpan().Clear();
        }
    }

    private async Task<Result<Unit, string>> ExecuteSignInFlowAsync(string mobileNumber, byte[] passwordBytes,
        uint connectId)
    {
        byte[] serverPublicKeyBytes = ServerPublicKey();
        OpaqueClient opaqueClient = GetOrCreateOpaqueClient(serverPublicKeyBytes);

        using KeyExchangeResult ke1Result = opaqueClient.GenerateKE1(passwordBytes);

        OpaqueSignInInitRequest initRequest = new()
        {
            MobileNumber = mobileNumber,
            PeerOprf = ByteString.CopyFrom(ke1Result.KeyExchangeData),
        };

        Result<OpaqueSignInInitResponse, string> initResult = await SendInitRequestAsync(initRequest, connectId);
        if (initResult.IsErr)
        {
            return Result<Unit, string>.Err(initResult.UnwrapErr());
        }

        OpaqueSignInInitResponse initResponse = initResult.Unwrap();

        Result<Unit, ValidationFailure> validationResult = ValidateInitResponse(initResponse);
        if (validationResult.IsErr)
        {
            return Result<Unit, string>.Err(validationResult.UnwrapErr().Message);
        }

        byte[] ke2Data = initResponse.ServerStateToken.ToByteArray();
        byte[] ke3Data = opaqueClient.GenerateKe3(ke2Data, ke1Result);

        byte[] baseSessionKey = opaqueClient.DeriveSessionKey(ke1Result);

        Result<byte[], string> enhancedKeyResult = await enhancedKeyDerivation.DeriveEnhancedKeyAsync(
            baseSessionKey,
            "ecliptix-signin-session",
            connectId,
            new KeyDerivationOptions
            {
                MemorySize = 262144,
                Iterations = 4,
                DegreeOfParallelism = 4,
                UseHardwareEntropy = true,
                OutputLength = 64
            });

        if (enhancedKeyResult.IsErr)
        {
            Log.Error("Failed to derive enhanced session key: {Error}", enhancedKeyResult.UnwrapErr());
            return Result<Unit, string>.Err("Failed to derive enhanced session key");
        }

        byte[] enhancedSessionKey = enhancedKeyResult.Unwrap();
        CryptographicOperations.ZeroMemory(baseSessionKey);

        OpaqueSignInFinalizeRequest finalizeRequest = new()
        {
            MobileNumber = mobileNumber,
            ClientMac =  ByteString.CopyFrom(ke3Data),
        };

        Result<SignInResult, string> finalResult = await SendFinalizeRequestAndVerifyAsync(finalizeRequest, enhancedSessionKey, connectId);

        if (finalResult.IsOk)
        {
            SignInResult signInResult = finalResult.Unwrap();

            // Store session key shares for current session (indexed by connectId)
            // This is optional since session keys are ephemeral
            Log.Debug("Splitting and storing session key using Shamir's Secret Sharing for connection {ConnectId}", connectId);
            Result<KeySplitResult, string> splitResult = await keySplitter.SplitKeyAsync(
                signInResult.SessionKey,
                threshold: 3,
                totalShares: 5);

            if (splitResult.IsOk)
            {
                using KeySplitResult splitKeys = splitResult.Unwrap();

                Result<Unit, string> storeResult = await multiLocationKeyStorage.StoreKeySharesAsync(splitKeys, connectId);
                if (storeResult.IsErr)
                {
                    Log.Warning("Failed to store session key shares: {Error}", storeResult.UnwrapErr());
                    // Continue - session key storage is optional
                }
            }
            else
            {
                Log.Warning("Failed to split session key: {Error}", splitResult.UnwrapErr());
                // Continue - session key storage is optional
            }

            await sessionKeyService.StoreSessionKeyAsync(signInResult.SessionKey, connectId);

            if (signInResult.Membership != null)
            {
                ByteString membershipIdentifier = signInResult.Membership.UniqueIdentifier;

                if (!ValidateMembershipIdentifier(membershipIdentifier))
                {
                    await systemEvents.NotifySystemStateAsync(SystemState.FatalError, "Invalid membership identifier");
                    return Result<Unit, string>.Err(localizationService[AuthenticationConstants.InvalidCredentialsKey]);
                }

                byte[] masterKey = MasterKeyDerivation.DeriveMasterKey(enhancedSessionKey, membershipIdentifier);
                string memberId = Helpers.FromByteStringToGuid(membershipIdentifier).ToString();

                // Store master key using Shamir's Secret Sharing indexed by membershipId
                Log.Information("Splitting and storing master key using Shamir's Secret Sharing for member {MemberId}", memberId);

                // Generate HMAC key for share validation
                Result<byte[], string> hmacKeyResult = await shareAuthenticationService.GenerateHmacKeyAsync(memberId);
                byte[]? hmacKey = hmacKeyResult.IsOk ? hmacKeyResult.Unwrap() : null;

                if (hmacKeyResult.IsErr)
                {
                    Log.Warning("Failed to generate HMAC key for member {MemberId}: {Error}", memberId, hmacKeyResult.UnwrapErr());
                }

                Result<KeySplitResult, string> masterSplitResult = await keySplitter.SplitKeyAsync(
                    masterKey,
                    threshold: 3,
                    totalShares: 5,
                    hmacKey: hmacKey);

                if (masterSplitResult.IsOk)
                {
                    using KeySplitResult masterSplitKeys = masterSplitResult.Unwrap();

                    // Store master key shares indexed by membershipId (persistent)
                    Result<Unit, string> masterStoreResult = await multiLocationKeyStorage.StoreKeySharesAsync(masterSplitKeys, memberId);
                    if (masterStoreResult.IsErr)
                    {
                        Log.Error("Failed to store master key shares for member {MemberId}: {Error}", memberId, masterStoreResult.UnwrapErr());
                        // Continue anyway - we still have identityService as fallback
                    }
                    else
                    {
                        Log.Information("Successfully stored master key shares for member {MemberId}", memberId);
                    }
                }
                else
                {
                    Log.Error("Failed to split master key for member {MemberId}: {Error}", memberId, masterSplitResult.UnwrapErr());
                }

                // Clean up HMAC key
                if (hmacKey != null)
                {
                    CryptographicOperations.ZeroMemory(hmacKey);
                }

                // Still store via identityService for backward compatibility
                await identityService.StoreIdentityAsync(masterKey, memberId);

                CryptographicOperations.ZeroMemory(masterKey);

                await applicationSecureStorageProvider.SetApplicationMembershipAsync(signInResult.Membership);
            }

            CryptographicOperations.ZeroMemory(enhancedSessionKey);
            return Result<Unit, string>.Ok(Unit.Value);
        }

        CryptographicOperations.ZeroMemory(enhancedSessionKey);
        return Result<Unit, string>.Err(finalResult.UnwrapErr());
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

    private async Task<Result<OpaqueSignInInitResponse, string>> SendInitRequestAsync(
        OpaqueSignInInitRequest initRequest, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInInitResponse> responseCompletionSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInInitRequest,
            SecureByteStringInterop.WithByteStringAsSpan(initRequest.ToByteString(), span => span.ToArray()),
            initResponsePayload =>
            {
                try
                {
                    OpaqueSignInInitResponse response = Helpers.ParseFromBytes<OpaqueSignInInitResponse>(initResponsePayload);
                    responseCompletionSource.TrySetResult(response);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseCompletionSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                }
            }, false, CancellationToken.None, waitForRecovery: true
        );

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            responseCompletionSource.TrySetException(new InvalidOperationException(failure.Message));
            return Result<OpaqueSignInInitResponse, string>.Err(string.Empty);
        }

        try
        {
            OpaqueSignInInitResponse response = await responseCompletionSource.Task;
            return Result<OpaqueSignInInitResponse, string>.Ok(response);
        }
        catch (Exception ex)
        {
            return Result<OpaqueSignInInitResponse, string>.Err($"{AuthenticationConstants.GetResponseFailurePrefix}{ex.Message}");
        }
    }

    private async Task<Result<SignInResult, string>> SendFinalizeRequestAndVerifyAsync(OpaqueSignInFinalizeRequest finalizeRequest,
        byte[] sessionKey, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInFinalizeResponse> responseCompletionSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInCompleteRequest,
            SecureByteStringInterop.WithByteStringAsSpan(finalizeRequest.ToByteString(), span => span.ToArray()),
            finalizeResponsePayload =>
            {
                try
                {
                    OpaqueSignInFinalizeResponse response = Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(finalizeResponsePayload);
                    responseCompletionSource.TrySetResult(response);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseCompletionSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                }
            }, false, CancellationToken.None, waitForRecovery: true
        );

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            responseCompletionSource.TrySetException(new InvalidOperationException(failure.Message));
            return Result<SignInResult, string>.Err(failure.Message);
        }

        OpaqueSignInFinalizeResponse capturedResponse;
        try
        {
            capturedResponse = await responseCompletionSource.Task;
        }
        catch (Exception ex)
        {
            return Result<SignInResult, string>.Err($"{AuthenticationConstants.GetResponseFailurePrefix}{ex.Message}");
        }

        if (capturedResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
        {
            string message = capturedResponse.HasMessage
                ? capturedResponse.Message
                : localizationService[AuthenticationConstants.InvalidCredentialsKey];
            return Result<SignInResult, string>.Err(message);
        }

        byte[] serverSessionKey = capturedResponse.SessionKey != null && !capturedResponse.SessionKey.IsEmpty
            ? capturedResponse.SessionKey.ToByteArray()
            : sessionKey;

        SignInResult result = new(serverSessionKey, capturedResponse.Membership);
        return Result<SignInResult, string>.Ok(result);
    }

    private bool ValidateMembershipIdentifier(ByteString identifier)
    {
        if (identifier.Length != 16)
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

        return hasNonZero && zeroCount <= 12;
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