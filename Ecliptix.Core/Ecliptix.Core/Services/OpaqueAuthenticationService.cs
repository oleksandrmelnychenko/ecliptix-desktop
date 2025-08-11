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
        OpaqueProtocolService clientOpaqueService = CreateOpaqueService();
        Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> oprfResult =
            OpaqueProtocolService.CreateOprfRequest(passwordBytes);
        if (oprfResult.IsErr)
        {
            OpaqueFailure opaqueError = oprfResult.UnwrapErr();
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError, opaqueError.Message));
            Result<byte[], string> errorResult = Result<byte[], string>.Err(localizationService["Common.Unexpected"]);
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
        OpaqueSignInInitResponse? capturedResponse = null;

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteServiceRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInInitRequest,
            initRequest.ToByteArray(),
            ServiceFlowType.Single,
            async initResponsePayload =>
            {
                capturedResponse = Helpers.ParseFromBytes<OpaqueSignInInitResponse>(initResponsePayload);
                return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None
        );

        if (networkResult.IsErr)
        {
            return Result<OpaqueSignInInitResponse, string>.Err(networkResult.UnwrapErr().Message);
        }

        return capturedResponse == null
            ? Result<OpaqueSignInInitResponse, string>.Err(localizationService["Common.Unexpected"])
            : Result<OpaqueSignInInitResponse, string>.Ok(capturedResponse);
    }

    private async Task<Result<byte[], string>> SendFinalizeRequestAndVerifyAsync(
        OpaqueProtocolService clientOpaqueService,
        OpaqueSignInFinalizeRequest finalizeRequest,
        byte[] sessionKey,
        byte[] serverMacKey,
        byte[] transcriptHash, uint connectId)
    {
        OpaqueSignInFinalizeResponse? capturedResponse = null;

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteServiceRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInCompleteRequest,
            finalizeRequest.ToByteArray(),
            ServiceFlowType.Single,
            async finalizeResponsePayload =>
            {
                capturedResponse = Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(finalizeResponsePayload);
                return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }, false, CancellationToken.None
        );

        if (networkResult.IsErr)
        {
            return Result<byte[], string>.Err(networkResult.UnwrapErr().Message);
        }

        if (capturedResponse == null)
        {
            return Result<byte[], string>.Err(localizationService["Common.Unexpected"]);
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