using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.OPAQUE.Agent;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Protobuf.Membership;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Unit = Ecliptix.Utilities.Unit;
using OtpCountdownStatus = Ecliptix.Protobuf.Membership.OtpCountdownUpdate.Types.Status;

namespace Ecliptix.Feature.Authentication.Services.Authentication;

public sealed class SecureKeyRecoveryService(
    NetworkProvider networkProvider,
    IOpaqueRegistrationService registrationService,
    ILocalizationService localizationService)
    : ISecureKeyRecoveryService, IDisposable
{
    private readonly Lock _opaqueAgentLock = new();
    private Option<OpaqueAgent> _opaqueAgent = Option<OpaqueAgent>.None;
    private byte[]? _cachedServerPublicKey;
    private bool _disposed;

    public async Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        Result<MobileNumberValidateResponse, string> result =
            await registrationService
                .ValidateMobileForRecoveryAsync(mobileNumber, connectId, cancellationToken)
                .ConfigureAwait(false);

        if (result.IsErr)
        {
            return Result<ByteString, string>.Err(result.UnwrapErr());
        }

        MobileNumberValidateResponse response = result.Unwrap();

        if (response.Result == OtpVerificationResult.InvalidMobile)
        {
            return Result<ByteString, string>.Err(response.Message);
        }

        if (response.MobileNumberId.IsEmpty)
        {
            return Result<ByteString, string>.Err(
                localizationService[AuthenticationConstants.MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY]);
        }

        return Result<ByteString, string>.Ok(response.MobileNumberId);
    }

    public Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default) =>
        registrationService.InitiateOtpVerificationAsync(
            mobileNumberIdentifier,
            OtpVerificationPurpose.PasswordRecovery,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default) =>
        registrationService.ResendOtpVerificationAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<MembershipProto, string>> VerifySecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        registrationService.VerifyOtpAsync(sessionIdentifier, otpCode, connectId,
            cancellationToken);

    public async Task<Result<Unit, string>> CompleteSecureKeyResetAsync(
        ByteString membershipIdentifier,
        SecureTextBuffer newSecureKey,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        if (newSecureKey.Length == 0)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SECURE_KEY_REQUIRED_KEY]);
        }

        RegistrationResult? registrationResult = null;

        try
        {
            Result<OpaqueAgent, string> opaqueAgentResult =
                await GetOrCreateOpaqueAgentAsync(connectId).ConfigureAwait(false);
            if (opaqueAgentResult.IsErr)
            {
                return Result<Unit, string>.Err(opaqueAgentResult.UnwrapErr());
            }

            OpaqueAgent opaqueAgent = opaqueAgentResult.Unwrap();

            Result<RegistrationResult, string> requestResult =
                CreateSecureKeyRecoveryRequest(opaqueAgent, newSecureKey);

            if (requestResult.IsErr)
            {
                return Result<Unit, string>.Err(requestResult.UnwrapErr());
            }

            registrationResult = requestResult.Unwrap();

            Result<OpaqueRecoveryInitResponse, string> initResult =
                await InitiateSecureKeyRecoveryAsync(membershipIdentifier, registrationResult.GetRequestCopy(),
                    connectId, cancellationToken).ConfigureAwait(false);

            if (initResult.IsErr)
            {
                return Result<Unit, string>.Err(initResult.UnwrapErr());
            }

            OpaqueRecoveryInitResponse initResponse = initResult.Unwrap();

            Result<Unit, string> processResult =
                await ProcessSecureKeyRecoveryInitResponse(initResponse).ConfigureAwait(false);

            if (processResult.IsErr)
            {
                return processResult;
            }

            return await FinalizeSecureKeyRecoveryAsync(opaqueAgent, initResponse, registrationResult,
                membershipIdentifier, connectId, cancellationToken).ConfigureAwait(false);
        }
        catch (OpaqueException)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]);
        }
        catch (Exception ex)
        {
            return Result<Unit, string>.Err(ex.Message);
        }
        finally
        {
            registrationResult?.Dispose();
        }
    }

    public Task<Result<Unit, string>> CleanupSecureKeyResetSessionAsync(Guid sessionIdentifier) =>
        registrationService.CleanupVerificationSessionAsync(sessionIdentifier);

    private static void CleanupSensitiveRecoveryData(
        byte[]? secureKeyCopy,
        byte[]? serverRecoveryResponse,
        byte[]? recoveryRecord,
        byte[]? masterKey)
    {
        if (secureKeyCopy is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(secureKeyCopy);
        }

        if (serverRecoveryResponse is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(serverRecoveryResponse);
        }

        if (recoveryRecord is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(recoveryRecord);
        }

        if (masterKey is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    private Result<RegistrationResult, string> CreateSecureKeyRecoveryRequest(
        OpaqueAgent opaqueAgent,
        SecureTextBuffer newSecureKey)
    {
        try
        {
            RegistrationResult registrationResult = null!;

            newSecureKey.WithSecureBytes(secureKeyBytes =>
            {
                byte[] secureKeyCopy = secureKeyBytes.ToArray();
                try
                {
                    registrationResult = opaqueAgent.CreateRegistrationRequest(secureKeyCopy);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secureKeyCopy);
                }
            });

            return Result<RegistrationResult, string>.Ok(registrationResult);
        }
        catch (OpaqueException)
        {
            return Result<RegistrationResult, string>.Err(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]);
        }
        catch (Exception ex)
        {
            return Result<RegistrationResult, string>.Err(
                $"{localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]}: {ex.Message}");
        }
    }

    private Task<Result<Unit, string>> ProcessSecureKeyRecoveryInitResponse(
        OpaqueRecoveryInitResponse initResponse)
    {
        if (initResponse.Result != OpaqueOperationResult.Succeeded)
        {
            string errorMessage = initResponse.Result switch
            {
                OpaqueOperationResult.InvalidCredentials =>
                    localizationService[AuthenticationConstants.INVALID_CREDENTIALS_KEY],
                _ => localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]
            };
            return Task.FromResult(Result<Unit, string>.Err(errorMessage));
        }

        return Task.FromResult(Result<Unit, string>.Ok(Unit.Value));
    }

    private async Task<Result<Unit, string>> FinalizeSecureKeyRecoveryAsync(
        OpaqueAgent opaqueAgent,
        OpaqueRecoveryInitResponse initResponse,
        RegistrationResult registrationResult,
        ByteString membershipIdentifier,
        uint connectId,
        CancellationToken cancellationToken)
    {
        byte[]? serverRecoveryResponse = null;
        byte[]? recoveryRecord = null;

        try
        {
            serverRecoveryResponse =
                SecureByteStringInterop.WithByteStringAsSpan(initResponse.PeerOprf, span => span.ToArray());

            recoveryRecord = opaqueAgent.FinalizeRegistration(serverRecoveryResponse, registrationResult);

            OpaqueRecoveryCompleteRequest completeRequest = new()
            {
                PeerRecoveryRecord = ByteString.CopyFrom(recoveryRecord),
                MembershipId = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRecoveryCompleteResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.RecoveryComplete,
                SecureByteStringInterop.WithByteStringAsSpan(completeRequest.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    OpaqueRecoveryCompleteResponse response =
                        Helpers.ParseFromBytes<OpaqueRecoveryCompleteResponse>(payload);
                    responseSource.TrySetResult(response);

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }, allowDuplicates: true, token: cancellationToken).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return Result<Unit, string>.Err(networkResult.UnwrapErr().Message);
            }

            await responseSource.Task.ConfigureAwait(false);
            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (OpaqueException)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]);
        }
        catch (ArgumentException)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]);
        }
        finally
        {
            CleanupSensitiveRecoveryData(null, serverRecoveryResponse, recoveryRecord, null);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_opaqueAgentLock)
        {
            _opaqueAgent.Do(client => client.Dispose());
            _opaqueAgent = Option<OpaqueAgent>.None;
        }

        _disposed = true;
    }

    private async Task<Result<OpaqueAgent, string>> GetOrCreateOpaqueAgentAsync(uint connectId)
    {
        Result<byte[], NetworkFailure> serverKeyResult =
            await networkProvider.GetServerPublicKeyAsync(connectId).ConfigureAwait(false);
        if (serverKeyResult.IsErr)
        {
            return Result<OpaqueAgent, string>.Err(
                $"Failed to get server public key: {serverKeyResult.UnwrapErr().Message}");
        }

        byte[] serverPublicKey = serverKeyResult.Unwrap();

        lock (_opaqueAgentLock)
        {
            if (_opaqueAgent.IsSome && _cachedServerPublicKey != null &&
                CryptographicOperations.FixedTimeEquals(serverPublicKey, _cachedServerPublicKey))
            {
                return Result<OpaqueAgent, string>.Ok(_opaqueAgent.Value!);
            }

            _opaqueAgent.Do(client => client.Dispose());
            try
            {
                OpaqueAgent newAgent = new(serverPublicKey);
                _opaqueAgent = Option<OpaqueAgent>.Some(newAgent);
                _cachedServerPublicKey = (byte[])serverPublicKey.Clone();

                return Result<OpaqueAgent, string>.Ok(newAgent);
            }
            catch (OpaqueException)
            {
                _opaqueAgent = Option<OpaqueAgent>.None;
                _cachedServerPublicKey = null;
                return Result<OpaqueAgent, string>.Err(
                    localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]);
            }
            catch (ArgumentException)
            {
                _opaqueAgent = Option<OpaqueAgent>.None;
                _cachedServerPublicKey = null;
                return Result<OpaqueAgent, string>.Err(
                    localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]);
            }
        }
    }

    private async Task<Result<OpaqueRecoveryInitResponse, string>> InitiateSecureKeyRecoveryAsync(
        ByteString membershipIdentifier,
        byte[] recoveryRequest,
        uint connectId,
        CancellationToken cancellationToken)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<OpaqueRecoveryInitResponse, string>.Err(
                localizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        try
        {
            OpaqueRecoveryInitRequest request = new()
            {
                PeerOprf = ByteString.CopyFrom(recoveryRequest),
                MembershipId = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRecoveryInitResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.RecoveryInit,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
                {
                    OpaqueRecoveryInitResponse response =
                        Helpers.ParseFromBytes<OpaqueRecoveryInitResponse>(payload);
                    responseSource.TrySetResult(response);

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }, allowDuplicates: true, token: cancellationToken).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return Result<OpaqueRecoveryInitResponse, string>.Err(networkResult.UnwrapErr().Message);
            }

            OpaqueRecoveryInitResponse initResponse = await responseSource.Task.ConfigureAwait(false);
            return Result<OpaqueRecoveryInitResponse, string>.Ok(initResponse);
        }
        catch (Exception ex)
        {
            return Result<OpaqueRecoveryInitResponse, string>.Err(ex.Message);
        }
    }
}
