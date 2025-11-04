using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Security;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class SecureKeyRecoveryService(
    NetworkProvider networkProvider,
    IOpaqueRegistrationService registrationService,
    ILocalizationService localizationService,
    IServerPublicKeyProvider serverPublicKeyProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider)
    : ISecureKeyRecoveryService, IDisposable
{
    private readonly Lock _opaqueClientLock = new();
    private Option<OpaqueClient> _opaqueClient = Option<OpaqueClient>.None;
    private byte[]? _cachedServerPublicKey;
    private bool _disposed;

    public async Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        Result<ValidateMobileNumberResponse, string> result =
            await registrationService
                .ValidateMobileNumberAsync(mobileNumber, connectId, cancellationToken)
                .ConfigureAwait(false);

        if (result.IsErr)
        {
            return Result<ByteString, string>.Err(result.UnwrapErr());
        }

        ValidateMobileNumberResponse response = result.Unwrap();
        return Result<ByteString, string>.Ok(response.MobileNumberIdentifier);
    }

    public Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default) =>
        registrationService.InitiateOtpVerificationAsync(
            mobileNumberIdentifier,
            VerificationPurpose.SecureKeyRecovery,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default) =>
        registrationService.ResendOtpVerificationAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Protobuf.Membership.Membership, string>> VerifySecureKeyResetOtpAsync(
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
            OpaqueClient opaqueClient = GetOrCreateOpaqueClient();

            Result<RegistrationResult, string> requestResult =
                CreateSecureKeyRecoveryRequest(opaqueClient, newSecureKey);

            if (requestResult.IsErr)
            {
                return Result<Unit, string>.Err(requestResult.UnwrapErr());
            }

            registrationResult = requestResult.Unwrap();

            Result<OpaqueRecoverySecureKeyInitResponse, string> initResult =
                await InitiateSecureKeyRecoveryAsync(membershipIdentifier, registrationResult.GetRequestCopy(),
                    connectId, cancellationToken).ConfigureAwait(false);

            if (initResult.IsErr)
            {
                return Result<Unit, string>.Err(initResult.UnwrapErr());
            }

            OpaqueRecoverySecureKeyInitResponse initResponse = initResult.Unwrap();

            Result<Unit, string> processResult =
                await ProcessSecureKeyRecoveryInitResponse(initResponse).ConfigureAwait(false);

            if (processResult.IsErr)
            {
                return processResult;
            }

            return await FinalizeSecureKeyRecoveryAsync(opaqueClient, initResponse, registrationResult,
                membershipIdentifier, connectId, cancellationToken).ConfigureAwait(false);
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
        OpaqueClient opaqueClient,
        SecureTextBuffer newSecureKey)
    {
        try
        {
            RegistrationResult? registrationResult = null;

            newSecureKey.WithSecureBytes(secureKeyBytes =>
            {
                byte[] secureKeyCopy = secureKeyBytes.ToArray();
                try
                {
                    registrationResult = opaqueClient.CreateRegistrationRequest(secureKeyCopy);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(secureKeyCopy);
                }
            });

            if (registrationResult is null)
            {
                throw new InvalidOperationException("Registration result was not initialized");
            }

            return Result<RegistrationResult, string>.Ok(registrationResult);
        }
        catch (Exception ex)
        {
            return Result<RegistrationResult, string>.Err(
                $"{localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]}: {ex.Message}");
        }
    }

    private async Task<Result<Unit, string>> ProcessSecureKeyRecoveryInitResponse(
        OpaqueRecoverySecureKeyInitResponse initResponse)
    {
        if (initResponse.Result != OpaqueRecoverySecureKeyInitResponse.Types.RecoveryResult.Succeeded)
        {
            string errorMessage = initResponse.Result switch
            {
                OpaqueRecoverySecureKeyInitResponse.Types.RecoveryResult.InvalidCredentials =>
                    localizationService[AuthenticationConstants.INVALID_CREDENTIALS_KEY],
                _ => localizationService[AuthenticationConstants.REGISTRATION_FAILED_KEY]
            };
            return Result<Unit, string>.Err(errorMessage);
        }

        if (initResponse.Membership?.AccountUniqueIdentifier != null &&
            initResponse.Membership.AccountUniqueIdentifier.Length > 0)
        {
            await applicationSecureStorageProvider
                .SetCurrentAccountIdAsync(initResponse.Membership.AccountUniqueIdentifier)
                .ConfigureAwait(false);
        }

        return Result<Unit, string>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, string>> FinalizeSecureKeyRecoveryAsync(
        OpaqueClient opaqueClient,
        OpaqueRecoverySecureKeyInitResponse initResponse,
        RegistrationResult registrationResult,
        ByteString membershipIdentifier,
        uint connectId,
        CancellationToken cancellationToken)
    {
        byte[]? serverRecoveryResponse = null;
        byte[]? recoveryRecord = null;
        byte[]? masterKey = null;

        try
        {
            serverRecoveryResponse =
                SecureByteStringInterop.WithByteStringAsSpan(initResponse.PeerOprf, span => span.ToArray());

            (byte[] record, byte[] generatedMasterKey) =
                opaqueClient.FinalizeRegistration(serverRecoveryResponse, registrationResult);
            recoveryRecord = record;
            masterKey = generatedMasterKey;

            OpaqueRecoverySecretKeyCompleteRequest completeRequest = new()
            {
                PeerRecoveryRecord = ByteString.CopyFrom(recoveryRecord),
                MembershipIdentifier = membershipIdentifier,
                MasterKey = ByteString.CopyFrom(masterKey)
            };

            TaskCompletionSource<OpaqueRecoverySecretKeyCompleteResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.RecoverySecretKeyComplete,
                SecureByteStringInterop.WithByteStringAsSpan(completeRequest.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    OpaqueRecoverySecretKeyCompleteResponse response =
                        Helpers.ParseFromBytes<OpaqueRecoverySecretKeyCompleteResponse>(payload);
                    responseSource.TrySetResult(response);

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }, true, cancellationToken).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return Result<Unit, string>.Err(networkResult.UnwrapErr().Message);
            }

            await responseSource.Task.ConfigureAwait(false);
            return Result<Unit, string>.Ok(Unit.Value);
        }
        finally
        {
            CleanupSensitiveRecoveryData(null, serverRecoveryResponse, recoveryRecord, masterKey);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_opaqueClientLock)
        {
            _opaqueClient.Do(client => client.Dispose());
            _opaqueClient = Option<OpaqueClient>.None;
        }

        _disposed = true;
    }

    private OpaqueClient GetOrCreateOpaqueClient()
    {
        byte[] serverPublicKey = serverPublicKeyProvider.GetServerPublicKey();

        lock (_opaqueClientLock)
        {
            if (_opaqueClient.IsSome && _cachedServerPublicKey != null &&
                CryptographicOperations.FixedTimeEquals(serverPublicKey, _cachedServerPublicKey))
            {
                return _opaqueClient.Value!;
            }

            _opaqueClient.Do(client => client.Dispose());
            OpaqueClient newClient = new(serverPublicKey);
            _opaqueClient = Option<OpaqueClient>.Some(newClient);
            _cachedServerPublicKey = (byte[])serverPublicKey.Clone();

            return newClient;
        }
    }

    private async Task<Result<OpaqueRecoverySecureKeyInitResponse, string>> InitiateSecureKeyRecoveryAsync(
        ByteString membershipIdentifier,
        byte[] recoveryRequest,
        uint connectId,
        CancellationToken cancellationToken)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<OpaqueRecoverySecureKeyInitResponse, string>.Err(
                localizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        try
        {
            OpaqueRecoverySecureKeyInitRequest request = new()
            {
                PeerOprf = ByteString.CopyFrom(recoveryRequest),
                MembershipIdentifier = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRecoverySecureKeyInitResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.RecoverySecretKeyInit,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
                {
                    OpaqueRecoverySecureKeyInitResponse response =
                        Helpers.ParseFromBytes<OpaqueRecoverySecureKeyInitResponse>(payload);
                    responseSource.TrySetResult(response);

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }, true, cancellationToken).ConfigureAwait(false);

            if (networkResult.IsErr)
            {
                return Result<OpaqueRecoverySecureKeyInitResponse, string>.Err(networkResult.UnwrapErr().Message);
            }

            OpaqueRecoverySecureKeyInitResponse initResponse = await responseSource.Task.ConfigureAwait(false);
            return Result<OpaqueRecoverySecureKeyInitResponse, string>.Ok(initResponse);
        }
        catch (Exception ex)
        {
            return Result<OpaqueRecoverySecureKeyInitResponse, string>.Err(ex.Message);
        }
    }
}
