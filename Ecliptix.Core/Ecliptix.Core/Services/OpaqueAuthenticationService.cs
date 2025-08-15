using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services;

public class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEvents systemEvents)
    : IAuthenticationService
{
    public async Task<Result<byte[], string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword,
        uint connectId)
    {
        byte[]? passwordBytes = null;
        try
        {
            securePassword.WithSecureBytes(bytes => passwordBytes = bytes.ToArray());
            if (passwordBytes != null && passwordBytes.Length != 0)
                return await ExecuteSignInFlowAsync(mobileNumber, passwordBytes, connectId);
            string requiredError = localizationService["ValidationErrors.SecureKey.Required"];
            return await Task.FromResult(Result<byte[], string>.Err(requiredError));
        }
        finally
        {
            passwordBytes?.AsSpan().Clear();
        }
    }

    private async Task<Result<byte[], string>> ExecuteSignInFlowAsync(string mobileNumber, byte[] passwordBytes,
        uint connectId)
    {
        try
        {
            OpaqueProtocolService clientOpaqueService = CreateOpaqueService();
            Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> oprfResult =
                OpaqueProtocolService.CreateOprfRequest(passwordBytes);
            if (oprfResult.IsErr)
            {
                OpaqueFailure opaqueError = oprfResult.UnwrapErr();
                systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError, opaqueError.Message));
                Result<byte[], string> errorResult =
                    Result<byte[], string>.Err(localizationService["Common.Unexpected"]);
                return errorResult;
            }

            (byte[] oprfRequest, BigInteger blind) = oprfResult.Unwrap();
            OpaqueSignInInitRequest initRequest = new()
            {
                PhoneNumber = mobileNumber,
                PeerOprf = ByteString.CopyFrom(oprfRequest),
            };

            Result<OpaqueSignInInitResponse, string> initResult = await SendInitRequestAsync(initRequest, connectId);
            if (initResult.IsErr)
            {
                Result<byte[], string> errorResult = Result<byte[], string>.Err(initResult.UnwrapErr());
                return errorResult;
            }

            OpaqueSignInInitResponse initResponse = initResult.Unwrap();

            Result<Unit, ValidationFailure> validationResult = ValidateInitResponse(initResponse);
            if (validationResult.IsErr)
            {
                Result<byte[], string> errorResult = Result<byte[], string>.Err(validationResult.UnwrapErr().Message);
                return errorResult;
            }

            Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[]
                TranscriptHash), OpaqueFailure> finalizationResult =
                clientOpaqueService.CreateSignInFinalizationRequest(
                    mobileNumber, passwordBytes, initResponse, blind);

            if (finalizationResult.IsErr)
            {
                Result<byte[], string> errorResult = Result<byte[], string>.Err(
                    localizationService["ValidationErrors.SecureKey.InvalidCredentials"]);
                return errorResult;
            }

            (OpaqueSignInFinalizeRequest finalizeRequest, byte[] sessionKey, byte[] serverMacKey,
                byte[] transcriptHash) = finalizationResult.Unwrap();

            Result<byte[], string> finalResult = await SendFinalizeRequestAndVerifyAsync(
                clientOpaqueService, finalizeRequest, sessionKey, serverMacKey, transcriptHash, connectId);

            return finalResult;
        }
        catch (Exception)
        {
            return Result<byte[], string>.Err(localizationService["Common.Unexpected"]);
        }
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

    private OpaqueProtocolService CreateOpaqueService()
    {
        byte[] serverPublicKeyBytes = ServerPublicKey();
        ECPublicKeyParameters serverStaticPublicKeyParam = new(
            OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(serverPublicKeyBytes),
            OpaqueCryptoUtilities.DomainParams
        );
        return new OpaqueProtocolService(serverStaticPublicKeyParam);
    }

    private byte[] ServerPublicKey() =>
        networkProvider.ApplicationInstanceSettings.ServerPublicKey.ToByteArray();

    private async Task<Result<OpaqueSignInInitResponse, string>> SendInitRequestAsync(
        OpaqueSignInInitRequest initRequest, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInInitResponse> responseCompletionSource = new TaskCompletionSource<OpaqueSignInInitResponse>();
        
        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInInitRequest,
            initRequest.ToByteArray(),
            async initResponsePayload =>
            {
                try
                {
                    OpaqueSignInInitResponse response = Helpers.ParseFromBytes<OpaqueSignInInitResponse>(initResponsePayload);
                    responseCompletionSource.TrySetResult(response);
                    return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseCompletionSource.TrySetException(ex);
                    return await Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"Failed to parse response: {ex.Message}")));
                }
            }, false, CancellationToken.None, waitForRecovery: true
        );

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            responseCompletionSource.TrySetException(new InvalidOperationException(failure.Message));
            return Result<OpaqueSignInInitResponse, string>.Err(failure.Message);
        }

        try
        {
            OpaqueSignInInitResponse response = await responseCompletionSource.Task;
            return Result<OpaqueSignInInitResponse, string>.Ok(response);
        }
        catch (Exception ex)
        {
            return Result<OpaqueSignInInitResponse, string>.Err($"Failed to get response: {ex.Message}");
        }
    }

    private async Task<Result<byte[], string>> SendFinalizeRequestAndVerifyAsync(
        OpaqueProtocolService clientOpaqueService,
        OpaqueSignInFinalizeRequest finalizeRequest,
        byte[] sessionKey,
        byte[] serverMacKey,
        byte[] transcriptHash, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInFinalizeResponse> responseCompletionSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInCompleteRequest,
            finalizeRequest.ToByteArray(),
            async finalizeResponsePayload =>
            {
                try
                {
                    OpaqueSignInFinalizeResponse response = Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(finalizeResponsePayload);
                    responseCompletionSource.TrySetResult(response);
                    return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseCompletionSource.TrySetException(ex);
                    return await Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"Failed to parse response: {ex.Message}")));
                }
            }, false, CancellationToken.None, waitForRecovery: true
        );

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            responseCompletionSource.TrySetException(new InvalidOperationException(failure.Message));
            return Result<byte[], string>.Err(failure.Message);
        }

        OpaqueSignInFinalizeResponse capturedResponse;
        try
        {
            capturedResponse = await responseCompletionSource.Task;
        }
        catch (Exception ex)
        {
            return Result<byte[], string>.Err($"Failed to get response: {ex.Message}");
        }

        if (capturedResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
        {
            string message = capturedResponse.HasMessage
                ? capturedResponse.Message
                : localizationService["ValidationErrors.SecureKey.InvalidCredentials"];
            return Result<byte[], string>.Err(message);
        }

        Result<byte[], OpaqueFailure> verificationResult = clientOpaqueService.VerifyServerMacAndGetSessionKey(
            capturedResponse, sessionKey, serverMacKey, transcriptHash);

        if (!verificationResult.IsErr) return Result<byte[], string>.Ok(verificationResult.Unwrap());
        string errorMessage = localizationService["ValidationErrors.SecureKey.InvalidCredentials"];
        systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
            verificationResult.UnwrapErr().Message));
        return Result<byte[], string>.Err(errorMessage);
    }
}