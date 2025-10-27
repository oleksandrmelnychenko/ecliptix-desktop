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
    private OpaqueClient? _opaqueClient;
    private byte[]? _cachedServerPublicKey;
    private bool _disposed;

    public async Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(
        string mobileNumber,
        string deviceIdentifier,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        Result<ValidateMobileNumberResponse, string> result =
            await registrationService.ValidateMobileNumberAsync(mobileNumber, deviceIdentifier, connectId, cancellationToken).ConfigureAwait(false);

        if (result.IsErr)
        {
            return Result<ByteString, string>.Err(result.UnwrapErr());
        }

        ValidateMobileNumberResponse response = result.Unwrap();
        return Result<ByteString, string>.Ok(response.MobileNumberIdentifier);
    }

    public Task<Result<Unit, string>> InitiatePasswordResetOtpAsync(
        ByteString mobileNumberIdentifier,
        string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default)
    {
        return registrationService.InitiateOtpVerificationAsync(
            mobileNumberIdentifier,
            deviceIdentifier,
            VerificationPurpose.PasswordRecovery,
            onCountdownUpdate,
            cancellationToken);
    }

    public Task<Result<Unit, string>> ResendPasswordResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default)
    {
        return registrationService.ResendOtpVerificationAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            deviceIdentifier,
            onCountdownUpdate,
            cancellationToken);
    }

    public Task<Result<Protobuf.Membership.Membership, string>> VerifyPasswordResetOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        string deviceIdentifier,
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        return registrationService.VerifyOtpAsync(sessionIdentifier, otpCode, deviceIdentifier, connectId, cancellationToken);
    }

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
        byte[]? passwordCopy = null;
        byte[]? serverRecoveryResponse = null;
        byte[]? recoveryRecord = null;

        try
        {
            OpaqueClient opaqueClient = GetOrCreateOpaqueClient();

            newPassword.WithSecureBytes(passwordBytes =>
            {
                passwordCopy = passwordBytes.ToArray();
                registrationResult = opaqueClient.CreateRegistrationRequest(passwordCopy);
            });

            if (registrationResult == null)
            {
                return Result<Unit, string>.Err(localizationService[AuthenticationConstants.RegistrationFailedKey]);
            }

            Result<OpaqueRecoverySecureKeyInitResponse, string> initResult =
                await InitiatePasswordRecoveryAsync(membershipIdentifier, registrationResult.GetRequestCopy(), connectId, cancellationToken).ConfigureAwait(false);

            if (initResult.IsErr)
            {
                return Result<Unit, string>.Err(initResult.UnwrapErr());
            }

            OpaqueRecoverySecureKeyInitResponse initResponse = initResult.Unwrap();

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

            serverRecoveryResponse =
                SecureByteStringInterop.WithByteStringAsSpan(initResponse.PeerOprf, span => span.ToArray());

            recoveryRecord = opaqueClient.FinalizeRegistration(serverRecoveryResponse, registrationResult);

            OpaqueRecoverySecretKeyCompleteRequest completeRequest = new()
            {
                PeerRecoveryRecord = ByteString.CopyFrom(recoveryRecord),
                MembershipIdentifier = membershipIdentifier
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
                        Serilog.Log.Error(ex, "[PASSWORD-RECOVERY-COMPLETE] Failed to parse password recovery complete response");
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
        catch (Exception ex)
        {
            return Result<Unit, string>.Err(ex.Message);
        }
        finally
        {
            registrationResult?.Dispose();

            if (passwordCopy != null)
            {
                CryptographicOperations.ZeroMemory(passwordCopy);
            }

            if (serverRecoveryResponse != null)
            {
                CryptographicOperations.ZeroMemory(serverRecoveryResponse);
            }

            if (recoveryRecord != null)
            {
                CryptographicOperations.ZeroMemory(recoveryRecord);
            }
        }
    }

    public Task<Result<Unit, string>> CleanupPasswordResetSessionAsync(Guid sessionIdentifier)
    {
        return registrationService.CleanupVerificationSessionAsync(sessionIdentifier);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_opaqueClientLock)
        {
            _opaqueClient?.Dispose();
            _opaqueClient = null;
        }

        _disposed = true;
    }

    private OpaqueClient GetOrCreateOpaqueClient()
    {
        byte[] serverPublicKey = serverPublicKeyProvider.GetServerPublicKey();

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
                PeerOprf = ByteString.CopyFrom(recoveryRequest),
                MembershipIdentifier = membershipIdentifier
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
                        Serilog.Log.Error(ex, "[PASSWORD-RECOVERY-INIT] Failed to parse password recovery init response");
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
