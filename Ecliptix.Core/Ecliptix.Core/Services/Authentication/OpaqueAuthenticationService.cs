using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
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
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

public record SignInResult(byte[] SessionKey, Ecliptix.Protobuf.Membership.Membership? Membership);

public class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEventService systemEvents,
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISessionKeyService sessionKeyService)
    : IAuthenticationService
{
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
        using OpaqueClient opaqueClient = new();

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
        byte[] ke3Data = opaqueClient.GenerateKE3(ke2Data, ke1Result);

        byte[] sessionKey = opaqueClient.DeriveSessionKey(ke1Result);

        OpaqueSignInFinalizeRequest finalizeRequest = new()
        {
            MobileNumber = mobileNumber,
            ServerStateToken = ByteString.CopyFrom(ke3Data)
        };

        Result<SignInResult, string> finalResult = await SendFinalizeRequestAndVerifyAsync(finalizeRequest, sessionKey, connectId);

        if (finalResult.IsOk)
        {
            SignInResult signInResult = finalResult.Unwrap();

            await sessionKeyService.StoreSessionKeyAsync(signInResult.SessionKey, connectId);

            if (signInResult.Membership != null)
            {
                ByteString membershipIdentifier = signInResult.Membership.UniqueIdentifier;

                if (!ValidateMembershipIdentifier(membershipIdentifier))
                {
                    await systemEvents.NotifySystemStateAsync(SystemState.FatalError, "Invalid membership identifier");
                    return Result<Unit, string>.Err(localizationService[AuthenticationConstants.InvalidCredentialsKey]);
                }

                // For now, use session key as master key derivation input until export key is implemented
                byte[] masterKey = MasterKeyDerivation.DeriveMasterKey(sessionKey, membershipIdentifier);
                string memberId = Helpers.FromByteStringToGuid(membershipIdentifier).ToString();
                await identityService.StoreIdentityAsync(masterKey, memberId);
                CryptographicOperations.ZeroMemory(masterKey);

                await applicationSecureStorageProvider.SetApplicationMembershipAsync(signInResult.Membership);
            }

            return Result<Unit, string>.Ok(Unit.Value);
        }

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


    private byte[] ServerPublicKey() =>
        SecureByteStringInterop.WithByteStringAsSpan(
            networkProvider.ApplicationInstanceSettings.ServerPublicKey,
            span => span.ToArray());

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

        // In proper OPAQUE flow, session key is already established from KE1/KE2/KE3
        byte[] serverSessionKey = capturedResponse.SessionKey != null && !capturedResponse.SessionKey.IsEmpty
            ? capturedResponse.SessionKey.ToByteArray()
            : sessionKey;

        SignInResult result = new(serverSessionKey, capturedResponse.Membership);
        return Result<SignInResult, string>.Ok(result);
    }

    private bool ValidateMembershipIdentifier(ByteString identifier)
    {
        if (identifier == null || identifier.Length != 16)
            return false;

        try
        {
            Guid memberGuid = Helpers.FromByteStringToGuid(identifier);
            if (memberGuid == Guid.Empty)
                return false;

            byte[] bytes = identifier.ToByteArray();
            int zeroCount = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 0) zeroCount++;
            }

            if (zeroCount > 12)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }
}