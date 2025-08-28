using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

public class OpaqueAuthenticationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService,
    ISystemEventService systemEvents)
    : IAuthenticationService
{
    public async Task<Result<byte[], string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword,
        uint connectId)
    {
        Serilog.Log.Information("🔐 OPAQUE SignInAsync: Starting authentication for mobile: {MobileNumber}, connectId: {ConnectId}", 
            mobileNumber?.Length > 0 ? $"{mobileNumber[..3]}***{mobileNumber[^3..]}" : "empty", connectId);
        
        if (string.IsNullOrEmpty(mobileNumber))
        {
            Serilog.Log.Warning("🔐 OPAQUE: Mobile number validation failed - null or empty");
            string mobileRequiredError = localizationService["ValidationErrors.MobileNumber.Required"];
            return Result<byte[], string>.Err(mobileRequiredError);
        }
        
        byte[]? passwordBytes = null;
        try
        {
            Serilog.Log.Information("🔐 OPAQUE: Extracting secure password bytes");
            securePassword.WithSecureBytes(bytes => passwordBytes = bytes.ToArray());
            
            if (passwordBytes != null && passwordBytes.Length != 0)
            {
                Serilog.Log.Information("🔐 OPAQUE: Password extracted successfully, length: {Length}", passwordBytes.Length);
                return await ExecuteSignInFlowAsync(mobileNumber, passwordBytes, connectId);
            }
            
            Serilog.Log.Warning("🔐 OPAQUE: Password validation failed - empty or null password");
            string requiredError = localizationService["ValidationErrors.SecureKey.Required"];
            return await Task.FromResult(Result<byte[], string>.Err(requiredError));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "🔐 OPAQUE: Unexpected error in SignInAsync");
            return Result<byte[], string>.Err(localizationService["Common.UnexpectedError"]);
        }
        finally
        {
            passwordBytes?.AsSpan().Clear();
        }
    }

    private async Task<Result<byte[], string>> ExecuteSignInFlowAsync(string mobileNumber, byte[] passwordBytes,
        uint connectId)
    {
        Serilog.Log.Information("🔐 OPAQUE ExecuteSignInFlowAsync: Starting OPAQUE flow for connectId: {ConnectId}", connectId);
        try
        {
            Serilog.Log.Information("🔐 OPAQUE: Creating OPAQUE service");
            OpaqueProtocolService clientOpaqueService = CreateOpaqueService();
            
            Serilog.Log.Information("🔐 OPAQUE: Creating OPRF request from password");
            Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> oprfResult;
            try 
            {
                oprfResult = OpaqueProtocolService.CreateOprfRequest(passwordBytes);
                Serilog.Log.Information("🔐 OPAQUE: CreateOprfRequest completed - Success: {IsSuccess}", oprfResult.IsOk);
            }
            catch (Exception ex) 
            {
                Serilog.Log.Error(ex, "🔐 OPAQUE: Exception in CreateOprfRequest");
                throw;
            }
            if (oprfResult.IsErr)
            {
                OpaqueFailure opaqueError = oprfResult.UnwrapErr();
                Serilog.Log.Error("🔐 OPAQUE: OPRF request creation failed: {Error}", opaqueError.Message);
                await systemEvents.NotifySystemStateAsync(SystemState.FatalError, opaqueError.Message);
                Result<byte[], string> errorResult =
                    Result<byte[], string>.Err(localizationService["Common.UnexpectedError"]);
                return errorResult;
            }

            Serilog.Log.Information("🔐 OPAQUE: OPRF request created successfully");

            (byte[] oprfRequest, BigInteger blind) = oprfResult.Unwrap();
            OpaqueSignInInitRequest initRequest = new()
            {
                MobileNumber = mobileNumber,
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
                TranscriptHash, byte[] ExportKey), OpaqueFailure> finalizationResult =
                clientOpaqueService.CreateSignInFinalizationRequest(
                    mobileNumber, passwordBytes, initResponse, blind);

            if (finalizationResult.IsErr)
            {
                Result<byte[], string> errorResult = Result<byte[], string>.Err(
                    localizationService["ValidationErrors.SecureKey.InvalidCredentials"]);
                return errorResult;
            }

            (OpaqueSignInFinalizeRequest finalizeRequest, byte[] sessionKey, byte[] serverMacKey,
                byte[] transcriptHash, byte[] exportKey) = finalizationResult.Unwrap();

            Result<byte[], string> finalResult = await SendFinalizeRequestAndVerifyAsync(finalizeRequest, sessionKey, serverMacKey, transcriptHash, connectId);


            CryptographicOperations.ZeroMemory(exportKey);

            return finalResult;
        }
        catch (Exception)
        {
            return Result<byte[], string>.Err(localizationService["Common.UnexpectedError"]);
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
        try
        {
            byte[] serverPublicKeyBytes = ServerPublicKey();
            Serilog.Log.Information("🔐 OPAQUE: Decoding server public key for AOT compatibility");
            
            Org.BouncyCastle.Math.EC.ECPoint serverPublicKeyPoint = OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(serverPublicKeyBytes);
            ECPublicKeyParameters serverStaticPublicKeyParam = new(
                serverPublicKeyPoint,
                OpaqueCryptoUtilities.DomainParams
            );
            
            Serilog.Log.Information("🔐 OPAQUE: Successfully created OPAQUE service");
            return new OpaqueProtocolService(serverStaticPublicKeyParam);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "🔐 OPAQUE: Failed to create OPAQUE service - DecodePoint failed in AOT mode");
            throw new InvalidOperationException("Failed to initialize OPAQUE protocol service", ex);
        }
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
            return Result<OpaqueSignInInitResponse, string>.Err(string.Empty);
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

    private async Task<Result<byte[], string>> SendFinalizeRequestAndVerifyAsync(OpaqueSignInFinalizeRequest finalizeRequest,
        byte[] sessionKey,
        byte[] serverMacKey,
        byte[] transcriptHash, uint connectId)
    {
        TaskCompletionSource<OpaqueSignInFinalizeResponse> responseCompletionSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.OpaqueSignInCompleteRequest,
            SecureByteStringInterop.WithByteStringAsSpan(finalizeRequest.ToByteString(), span => span.ToArray()),
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

        Result<byte[], OpaqueFailure> verificationResult = OpaqueProtocolService.VerifyServerMacAndGetSessionKey(
            capturedResponse, sessionKey, serverMacKey, transcriptHash);

        if (!verificationResult.IsErr) return Result<byte[], string>.Ok(verificationResult.Unwrap());
        string errorMessage = localizationService["ValidationErrors.SecureKey.InvalidCredentials"];
        await systemEvents.NotifySystemStateAsync(SystemState.FatalError, verificationResult.UnwrapErr().Message);
        return Result<byte[], string>.Err(errorMessage);
    }
}