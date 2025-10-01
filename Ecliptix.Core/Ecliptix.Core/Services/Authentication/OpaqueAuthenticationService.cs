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
    public Ecliptix.Protobuf.Membership.Membership? Membership { get; } = membership;

    public void Dispose()
    {
        MasterKeyHandle?.Dispose();
        MasterKeyHandle = null;
    }
}

public class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEventService systemEvents,
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IHardenedKeyDerivation hardenedKeyDerivation,
    IDistributedShareStorage distributedShareStorage,
    ISecretSharingService keySplitter,
    IHmacKeyManager hmacKeyManager)
    : IAuthenticationService, IDisposable
{
    private const int KeyDerivationMemorySize = 262144;
    private const int KeyDerivationOutputLength = 64;
    private const int KeyDerivationIterations = 4;
    private const int KeyDerivationParallelism = 4;
    private const int MinimumShareThreshold = 3;
    private const int TotalKeyShares = 5;
    private const int GuidByteLength = 16;
    private const int MaxAllowedZeroBytes = 12;
    private const string SignInSessionContext = "ecliptix-signin-session";
    private const int NetworkRequestTimeoutMs = 30000;

    private readonly Lock _opaqueClientLock = new();
    private OpaqueClient? _opaqueClient;
    private byte[]? _cachedServerPublicKey;
    private byte[]? _serverPublicKeyCache;

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
        MemorySize = KeyDerivationMemorySize,
        Iterations = KeyDerivationIterations,
        DegreeOfParallelism = KeyDerivationParallelism,
        UseHardwareEntropy = true,
        OutputLength = KeyDerivationOutputLength
    };
    
    private string GetOpaqueErrorMessage(OpaqueResult error)
    {
        return OpaqueErrorMessages.TryGetValue(error, out string? key)
            ? localizationService[key]
            : localizationService[AuthenticationConstants.CommonUnexpectedErrorKey];
    }
    
    private byte[] GetServerPublicKey()
    {
        if (_serverPublicKeyCache == null)
        {
            _serverPublicKeyCache = SecureByteStringInterop.WithByteStringAsSpan(
                networkProvider.ApplicationInstanceSettings.ServerPublicKey,
                span => span.ToArray());
        }
        return _serverPublicKeyCache;
    }

    private OpaqueClient GetOrCreateOpaqueClient(byte[] serverPublicKey)
    {
        lock (_opaqueClientLock)
        {
            if (_opaqueClient == null || _cachedServerPublicKey == null ||
                !serverPublicKey.AsSpan().SequenceEqual(_cachedServerPublicKey.AsSpan()))
            {
                _opaqueClient?.Dispose();
                _opaqueClient = new OpaqueClient(serverPublicKey);
                _cachedServerPublicKey = serverPublicKey;
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
                return await ExecuteSignInFlowAsync(mobileNumber, passwordBytes, connectId);
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
        byte[] serverPublicKeyBytes = GetServerPublicKey();
        OpaqueClient opaqueClient = GetOrCreateOpaqueClient(serverPublicKeyBytes);

        using KeyExchangeResult ke1Result = opaqueClient.GenerateKE1(passwordBytes);

        OpaqueSignInInitRequest initRequest = new()
        {
            MobileNumber = mobileNumber,
            PeerOprf = ByteString.CopyFrom(ke1Result.KeyExchangeData),
        };

        Result<OpaqueSignInInitResponse, AuthenticationFailure> initResult = await SendInitRequestAsync(initRequest, connectId);
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
                SignInSessionContext,
                DefaultKeyDerivationOptions);

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
            await SendFinalizeRequestAndVerifyAsync(finalizeRequest, enhancedMasterKeyHandle, connectId);

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

                if (!ValidateMembershipIdentifier(membershipIdentifier))
                {
                    await systemEvents.NotifySystemStateAsync(SystemState.FatalError, "Invalid membership identifier");
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.InvalidMembershipIdentifier(localizationService[AuthenticationConstants.InvalidCredentialsKey]));
                }

                Result<SodiumSecureMemoryHandle, SodiumFailure> masterKeyHandleResult =
                    MasterKeyDerivation.DeriveMasterKeyHandle(enhancedMasterKeyHandle, membershipIdentifier);

                if (masterKeyHandleResult.IsErr)
                {
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.MasterKeyDerivationFailed(masterKeyHandleResult.UnwrapErr().Message));
                }

                using SodiumSecureMemoryHandle masterKeyHandle = masterKeyHandleResult.Unwrap();
                Guid membershipId = Helpers.FromByteStringToGuid(membershipIdentifier);

                Result<SodiumSecureMemoryHandle, KeySplittingFailure> hmacKeyHandleResult =
                    await hmacKeyManager.GenerateHmacKeyHandleAsync(membershipId.ToString());

                if (hmacKeyHandleResult.IsErr)
                {
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.HmacKeyGenerationFailed(hmacKeyHandleResult.UnwrapErr().Message));
                }

                using SodiumSecureMemoryHandle hmacKeyHandle = hmacKeyHandleResult.Unwrap();

                Result<KeySplitResult, KeySplittingFailure> masterSplitResult = await keySplitter.SplitKeyAsync(
                    masterKeyHandle,
                    threshold: MinimumShareThreshold,
                    totalShares: TotalKeyShares,
                    hmacKeyHandle: hmacKeyHandle);

                if (masterSplitResult.IsOk)
                {
                    using KeySplitResult masterSplitKeys = masterSplitResult.Unwrap();

                    Result<Unit, KeySplittingFailure> masterStoreResult =
                        await distributedShareStorage.StoreKeySharesAsync(masterSplitKeys, membershipId);
                    if (masterStoreResult.IsErr)
                    {
                        await systemEvents.NotifySystemStateAsync(SystemState.Busy,
                            $"Failed to store key shares: {masterStoreResult.UnwrapErr().Message}");
                    }
                }

                await identityService.StoreIdentityAsync(masterKeyHandle, membershipId.ToString());
                await applicationSecureStorageProvider.SetApplicationMembershipAsync(signInResult.Membership);

                Result<Unit, NetworkFailure> recreateProtocolResult =
                    await networkProvider.RecreateProtocolWithMasterKeyAsync(
                        masterKeyHandle, membershipIdentifier, connectId);

                if (recreateProtocolResult.IsErr)
                {
                    return Result<Unit, AuthenticationFailure>.Err(
                        AuthenticationFailure.NetworkRequestFailed(
                            $"Failed to establish authenticated protocol: {recreateProtocolResult.UnwrapErr().Message}"));
                }
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

        using CancellationTokenSource timeoutCts = new(NetworkRequestTimeoutMs);
        await using CancellationTokenRegistration registration = timeoutCts.Token.Register(() =>
            responseCompletionSource.TrySetCanceled(timeoutCts.Token), useSynchronizationContext: false);

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
                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseCompletionSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding(
                            $"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                }
            }, false, CancellationToken.None, waitForRecovery: true
        );

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            responseCompletionSource.TrySetException(new InvalidOperationException(failure.Message));
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed(failure.Message));
        }

        try
        {
            OpaqueSignInInitResponse response = await responseCompletionSource.Task;
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Ok(response);
        }
        catch (OperationCanceledException)
        {
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed($"Sign-in initialization request timed out after {NetworkRequestTimeoutMs/1000} seconds"));
        }
        catch (Exception ex)
        {
            return Result<OpaqueSignInInitResponse, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed($"{AuthenticationConstants.GetResponseFailurePrefix}{ex.Message}", ex));
        }
    }

    private async Task<Result<SignInResult, AuthenticationFailure>> SendFinalizeRequestAndVerifyAsync(
        OpaqueSignInFinalizeRequest finalizeRequest,
        SodiumSecureMemoryHandle sessionKeyHandle, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInFinalizeResponse> responseCompletionSource = new();

        using CancellationTokenSource timeoutCts = new(NetworkRequestTimeoutMs);
        using CancellationTokenRegistration registration = timeoutCts.Token.Register(() =>
            responseCompletionSource.TrySetCanceled(timeoutCts.Token), useSynchronizationContext: false);

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
                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseCompletionSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding(
                            $"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                }
            }, false, CancellationToken.None, waitForRecovery: true
        );

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            responseCompletionSource.TrySetException(new InvalidOperationException(failure.Message));
            return Result<SignInResult, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed(failure.Message));
        }

        OpaqueSignInFinalizeResponse capturedResponse;
        try
        {
            capturedResponse = await responseCompletionSource.Task;
        }
        catch (OperationCanceledException)
        {
            return Result<SignInResult, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed($"Sign-in finalization request timed out after {NetworkRequestTimeoutMs/1000} seconds"));
        }
        catch (Exception ex)
        {
            return Result<SignInResult, AuthenticationFailure>.Err(
                AuthenticationFailure.NetworkRequestFailed($"{AuthenticationConstants.GetResponseFailurePrefix}{ex.Message}", ex));
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
        if (identifier.Length != GuidByteLength)
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