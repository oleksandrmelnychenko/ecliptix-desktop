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
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Unit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class PasswordRecoveryService(
    NetworkProvider networkProvider,
    IOpaqueRegistrationService registrationService,
    ILocalizationService localizationService,
    IServerPublicKeyProvider serverPublicKeyProvider,
    IApplicationSecureStorageProvider applicationSecureStorageProvider)
    : IPasswordRecoveryService, IDisposable
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

    public Task<Result<Unit, string>> InitiatePasswordResetOtpAsync(
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default) =>
        registrationService.InitiateOtpVerificationAsync(
            mobileNumberIdentifier,
            VerificationPurpose.PasswordRecovery,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Unit, string>> ResendPasswordResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default) =>
        registrationService.ResendOtpVerificationAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Protobuf.Membership.Membership, string>> VerifyPasswordResetOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        registrationService.VerifyOtpAsync(sessionIdentifier, otpCode, connectId,
            cancellationToken);

    public async Task<Result<Unit, string>> CompletePasswordResetAsync(
        ByteString membershipIdentifier,
        SecureTextBuffer newPassword,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
        }

        if (newPassword.Length == 0)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SecureKeyRequiredKey]);
        }

        RegistrationResult? registrationResult = null;

        try
        {
            OpaqueClient opaqueClient = GetOrCreateOpaqueClient();

            Result<RegistrationResult, string> requestResult =
                CreatePasswordRecoveryRequest(opaqueClient, newPassword);

            if (requestResult.IsErr)
            {
                return Result<Unit, string>.Err(requestResult.UnwrapErr());
            }

            registrationResult = requestResult.Unwrap();

            Result<OpaqueRecoverySecureKeyInitResponse, string> initResult =
                await InitiatePasswordRecoveryAsync(membershipIdentifier, registrationResult.GetRequestCopy(),
                    connectId, cancellationToken).ConfigureAwait(false);

            if (initResult.IsErr)
            {
                return Result<Unit, string>.Err(initResult.UnwrapErr());
            }

            OpaqueRecoverySecureKeyInitResponse initResponse = initResult.Unwrap();

            Result<Unit, string> processResult =
                await ProcessPasswordRecoveryInitResponse(initResponse, membershipIdentifier).ConfigureAwait(false);

            if (processResult.IsErr)
            {
                return processResult;
            }

            return await FinalizePasswordRecoveryAsync(opaqueClient, initResponse, registrationResult,
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

    public Task<Result<Unit, string>> CleanupPasswordResetSessionAsync(Guid sessionIdentifier) =>
        registrationService.CleanupVerificationSessionAsync(sessionIdentifier);

    private static void CleanupSensitiveRecoveryData(
        byte[]? passwordCopy,
        byte[]? serverRecoveryResponse,
        byte[]? recoveryRecord,
        byte[]? masterKey)
    {
        if (passwordCopy is { Length: > 0 })
        {
            CryptographicOperations.ZeroMemory(passwordCopy);
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

    private Result<RegistrationResult, string> CreatePasswordRecoveryRequest(
        OpaqueClient opaqueClient,
        SecureTextBuffer newPassword)
    {
        RegistrationResult? registrationResult = null;

        newPassword.WithSecureBytes(passwordBytes =>
        {
            byte[] passwordCopy = passwordBytes.ToArray();
            try
            {
                registrationResult = opaqueClient.CreateRegistrationRequest(passwordCopy);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordCopy);
            }
        });

        if (registrationResult == null)
        {
            return Result<RegistrationResult, string>.Err(
                localizationService[AuthenticationConstants.RegistrationFailedKey]);
        }

        return Result<RegistrationResult, string>.Ok(registrationResult);
    }

    private async Task<Result<Unit, string>> ProcessPasswordRecoveryInitResponse(
        OpaqueRecoverySecureKeyInitResponse initResponse,
        ByteString membershipIdentifier)
    {
        if (initResponse.Result != OpaqueRecoverySecureKeyInitResponse.Types.RecoveryResult.Succeeded)
        {
            string errorMessage = initResponse.Result switch
            {
                OpaqueRecoverySecureKeyInitResponse.Types.RecoveryResult.InvalidCredentials =>
                    localizationService[AuthenticationConstants.InvalidCredentialsKey],
                _ => localizationService[AuthenticationConstants.RegistrationFailedKey]
            };
            return Result<Unit, string>.Err(errorMessage);
        }

        if (initResponse.Membership?.AccountUniqueIdentifier != null &&
            initResponse.Membership.AccountUniqueIdentifier.Length > 0)
        {
            await applicationSecureStorageProvider
                .SetCurrentAccountIdAsync(initResponse.Membership.AccountUniqueIdentifier)
                .ConfigureAwait(false);
            Serilog.Log.Information(
                "[PASSWORD-RECOVERY-ACCOUNT] Active account stored from recovery response. MembershipId: {MembershipId}, AccountId: {AccountId}",
                Helpers.FromByteStringToGuid(membershipIdentifier),
                Helpers.FromByteStringToGuid(initResponse.Membership.AccountUniqueIdentifier));
        }

        return Result<Unit, string>.Ok(Unit.Value);
    }

    private async Task<Result<Unit, string>> FinalizePasswordRecoveryAsync(
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
                    try
                    {
                        OpaqueRecoverySecretKeyCompleteResponse response =
                            Helpers.ParseFromBytes<OpaqueRecoverySecretKeyCompleteResponse>(payload);
                        responseSource.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex,
                            "[PASSWORD-RECOVERY-COMPLETE] Failed to parse password recovery complete response");
                        responseSource.TrySetException(ex);
                    }

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
                serverPublicKey.AsSpan().SequenceEqual(_cachedServerPublicKey.AsSpan()))
            {
                return _opaqueClient.Value!;
            }

            _opaqueClient.Do(client => client.Dispose());
            OpaqueClient newClient = new OpaqueClient(serverPublicKey);
            _opaqueClient = Option<OpaqueClient>.Some(newClient);
            _cachedServerPublicKey = (byte[])serverPublicKey.Clone();

            return newClient;
        }
    }

    private async Task<Result<OpaqueRecoverySecureKeyInitResponse, string>> InitiatePasswordRecoveryAsync(
        ByteString membershipIdentifier,
        byte[] recoveryRequest,
        uint connectId,
        CancellationToken cancellationToken)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<OpaqueRecoverySecureKeyInitResponse, string>.Err(
                localizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
        }

        try
        {
            OpaqueRecoverySecureKeyInitRequest request = new()
            {
                PeerOprf = ByteString.CopyFrom(recoveryRequest), MembershipIdentifier = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRecoverySecureKeyInitResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.RecoverySecretKeyInit,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
                {
                    try
                    {
                        OpaqueRecoverySecureKeyInitResponse response =
                            Helpers.ParseFromBytes<OpaqueRecoverySecureKeyInitResponse>(payload);
                        responseSource.TrySetResult(response);
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Error(ex,
                            "[PASSWORD-RECOVERY-INIT] Failed to parse password recovery init response");
                        responseSource.TrySetException(ex);
                    }

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
