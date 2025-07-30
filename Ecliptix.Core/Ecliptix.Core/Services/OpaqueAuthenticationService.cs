using System;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
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
    public Task<Result<byte[], string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword)
    {
        byte[]? passwordBytes = null;
        try
        {
            securePassword.WithSecureBytes(bytes => passwordBytes = bytes.ToArray());
            if (passwordBytes == null || passwordBytes.Length == 0)
            {
                string requiredError = localizationService["ValidationErrors.SecureKey.Required"];
                return Task.FromResult(Result<byte[], string>.Err(requiredError));
            }

            return ExecuteSignInFlowAsync(mobileNumber, passwordBytes);
        }
        finally
        {
            passwordBytes?.AsSpan().Clear();
        }
    }

    private async Task<Result<byte[], string>> ExecuteSignInFlowAsync(string mobileNumber, byte[] passwordBytes)
    {
        OpaqueProtocolService clientOpaqueService = CreateOpaqueService();
        Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> oprfResult =
            OpaqueProtocolService.CreateOprfRequest(passwordBytes);
        if (oprfResult.IsErr)
        {
            OpaqueFailure opaqueError = oprfResult.UnwrapErr();
            systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError, opaqueError.Message));
            return Result<byte[], string>.Err(localizationService["Errors.Generic.Unexpected"]);
        }

        (byte[] oprfRequest, BigInteger blind) = oprfResult.Unwrap();
        OpaqueSignInInitRequest initRequest = new()
        {
            PhoneNumber = mobileNumber,
            PeerOprf = ByteString.CopyFrom(oprfRequest),
        };

        byte[]? capturedSessionKey = null;

        Result<Unit, NetworkFailure> flowResult = await networkProvider.ExecuteServiceRequestAsync(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceType.OpaqueSignInInitRequest,
            initRequest.ToByteArray(),
            ServiceFlowType.Single,
            onCompleted: async initResponsePayload =>
            {
                OpaqueSignInInitResponse initResponse =
                    Helpers.ParseFromBytes<OpaqueSignInInitResponse>(initResponsePayload);
                Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[]
                    TranscriptHash), OpaqueFailure> finalizationResult =
                    clientOpaqueService.CreateSignInFinalizationRequest(
                        mobileNumber, passwordBytes, initResponse, blind);

                if (finalizationResult.IsErr)
                {
                    string errorMessage = localizationService["ValidationErrors.SecureKey.InvalidCredentials"];
                    return Result<Unit, NetworkFailure>.Err(EcliptixProtocolFailure.Generic(errorMessage)
                        .ToNetworkFailure());
                }

                (OpaqueSignInFinalizeRequest finalizeRequest, byte[] sessionKey, byte[] serverMacKey,
                    byte[] transcriptHash) = finalizationResult.Unwrap();

                return await SendFinalizeRequestAndVerify(
                    clientOpaqueService, finalizeRequest, sessionKey, serverMacKey, transcriptHash,
                    onSuccess: finalKey => capturedSessionKey = finalKey);
            });

        if (flowResult.IsErr)
        {
            return Result<byte[], string>.Err(flowResult.UnwrapErr().Message);
        }

        return capturedSessionKey == null
            ? Result<byte[], string>.Err(localizationService["Errors.Generic.Unexpected"])
            : Result<byte[], string>.Ok(capturedSessionKey);
    }

    private async Task<Result<Unit, NetworkFailure>> SendFinalizeRequestAndVerify(
        OpaqueProtocolService clientOpaqueService,
        OpaqueSignInFinalizeRequest finalizeRequest,
        byte[] sessionKey,
        byte[] serverMacKey,
        byte[] transcriptHash,
        Action<byte[]> onSuccess)
    {
        return await networkProvider.ExecuteServiceRequestAsync(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceType.OpaqueSignInCompleteRequest,
            finalizeRequest.ToByteArray(),
            ServiceFlowType.Single,
            onCompleted: finalizeResponsePayload =>
            {
                OpaqueSignInFinalizeResponse finalizeResponse =
                    Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(finalizeResponsePayload);

                if (finalizeResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
                {
                    string message = finalizeResponse.HasMessage
                        ? finalizeResponse.Message
                        : localizationService["ValidationErrors.SecureKey.InvalidCredentials"];
                    return Task.FromResult(
                        Result<Unit, NetworkFailure>.Err(EcliptixProtocolFailure.Generic(message).ToNetworkFailure()));
                }

                Result<byte[], OpaqueFailure> verificationResult = clientOpaqueService.VerifyServerMacAndGetSessionKey(
                    finalizeResponse, sessionKey, serverMacKey, transcriptHash);

                if (verificationResult.IsErr)
                {
                    string errorMessage = localizationService["ValidationErrors.SecureKey.InvalidCredentials"];

                    systemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
                        verificationResult.UnwrapErr().Message));

                    return Task.FromResult(
                        Result<Unit, NetworkFailure>.Err(EcliptixProtocolFailure.Generic(errorMessage)
                            .ToNetworkFailure()));
                }

                onSuccess(verificationResult.Unwrap());
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            }
        );
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

    private uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        return Helpers.ComputeUniqueConnectId(
            networkProvider.ApplicationInstanceSettings.AppInstanceId.Span,
            networkProvider.ApplicationInstanceSettings.DeviceId.Span,
            pubKeyExchangeType);
    }

    private byte[] ServerPublicKey() =>
        networkProvider.ApplicationInstanceSettings.ServerPublicKey.ToByteArray();
}