using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Membership;

internal sealed class LogoutService(
    NetworkProvider networkProvider,
    IMessageBus messageBus,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IApplicationStateManager stateManager,
    IStateCleanupService stateCleanupService,
    IApplicationRouter router,
    IIdentityService identityService)
    : ILogoutService
{
    private readonly PendingLogoutRequestStorage _pendingLogoutRequestStorage = new(applicationSecureStorageProvider);
    private readonly LogoutProofHandler _logoutProofHandler = new(identityService, applicationSecureStorageProvider);

    public async Task<Result<Unit, LogoutFailure>> LogoutAsync(LogoutReason reason,
        CancellationToken cancellationToken = default)
    {
        Option<string> membershipIdOption = await GetCurrentMembershipIdAsync().ConfigureAwait(false);
        if (!membershipIdOption.HasValue)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        string membershipId = membershipIdOption.Value!;

        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Failed to get application settings",
                    new Exception(settingsResult.UnwrapErr().Message)));
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ByteString membershipIdBytes = Helpers.GuidToByteString(Guid.Parse(membershipId));

        LogoutRequest logoutRequest = new()
        {
            MembershipIdentifier = membershipIdBytes,
            LogoutReason = reason.ToString(),
            Timestamp = timestamp,
            Scope = LogoutScope.ThisDevice,
            AccountIdentifier = settings.CurrentAccountId
        };

        Result<Unit, LogoutFailure> hmacResult = await _logoutProofHandler.GenerateLogoutHmacProofAsync(logoutRequest, membershipId);
        if (hmacResult.IsErr)
        {
            Log.Warning("[LOGOUT] Failed to generate HMAC proof: {Error}", hmacResult.UnwrapErr().Message);
            return hmacResult;
        }

        uint connectId = NetworkProvider.ComputeUniqueConnectId(
            settings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Log.Information("[LOGOUT] Using existing authenticated protocol. ConnectId: {ConnectId}", connectId);

        TaskCompletionSource<Result<LogoutResponse, LogoutFailure>> responseCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.Logout,
            logoutRequest.ToByteArray(),
            responsePayload =>
            {
                try
                {
                    LogoutResponse logoutResponse = LogoutResponse.Parser.ParseFrom(responsePayload);

                    if (logoutResponse.Result != LogoutResponse.Types.Result.Succeeded)
                    {
                        Log.Warning("[LOGOUT] Server returned non-success status: {Status}", logoutResponse.Result);
                    }

                    responseCompletionSource.TrySetResult(MapLogoutResponse(logoutResponse));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[LOGOUT] Failed to parse logout response");
                    responseCompletionSource.TrySetResult(Result<LogoutResponse, LogoutFailure>.Err(
                        LogoutFailure.UnexpectedError("Failed to parse logout response", ex)));
                }

                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            },
            allowDuplicates: false,
            token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            Log.Warning("[LOGOUT] Network request failed: {Error}", failure.Message);

            Result<Unit, LogoutFailure> storeResult = await _pendingLogoutRequestStorage
                .StorePendingLogoutAsync(logoutRequest).ConfigureAwait(false);

            if (storeResult.IsErr)
            {
                Log.Warning("[LOGOUT] Failed to store pending logout request: {Error}",
                    storeResult.UnwrapErr().Message);
            }

            Log.Information("[LOGOUT] Network request failed, proceeding with local logout. Starting cleanup for MembershipId: {MembershipId}",
                membershipId);

            await CompleteLogoutWithCleanupAsync(membershipId, reason, connectId, cancellationToken).ConfigureAwait(false);

            Log.Information("[LOGOUT] Offline logout completed successfully. MembershipId: {MembershipId}", membershipId);

            return Result<Unit, LogoutFailure>.Ok(Unit.Value);
        }

        Result<LogoutResponse, LogoutFailure>
            responseResult = await responseCompletionSource.Task.ConfigureAwait(false);

        if (responseResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(responseResult.UnwrapErr());
        }

        LogoutResponse response = responseResult.Unwrap();

        if (response.Result == LogoutResponse.Types.Result.Succeeded)
        {
            Result<Unit, LogoutFailure> proofVerification =
                await _logoutProofHandler.VerifyRevocationProofAsync(response, membershipId, connectId);

            if (proofVerification.IsErr)
            {
                Log.Error("[LOGOUT] Revocation proof verification failed for MembershipId: {MembershipId}",
                    membershipId);
            }
        }

        Log.Information("[LOGOUT] Logout API call succeeded. Starting cleanup for MembershipId: {MembershipId}",
            membershipId);

        await CompleteLogoutWithCleanupAsync(membershipId, reason, connectId, cancellationToken).ConfigureAwait(false);

        Log.Information("[LOGOUT] Logout completed successfully. MembershipId: {MembershipId}", membershipId);

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    public static async Task<bool> HasRevocationProofAsync(IApplicationSecureStorageProvider storageProvider,
        string membershipId)
    {
        return await LogoutProofHandler.HasRevocationProofAsync(storageProvider, membershipId);
    }

    private static Result<LogoutResponse, LogoutFailure> MapLogoutResponse(LogoutResponse response)
    {
        return response.Result switch
        {
            LogoutResponse.Types.Result.Succeeded => Result<LogoutResponse, LogoutFailure>.Ok(response),
            LogoutResponse.Types.Result.AlreadyLoggedOut => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.AlreadyLoggedOut("Session is already logged out on the server")),
            LogoutResponse.Types.Result.SessionNotFound => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.SessionNotFound("Active session was not found on the server")),
            LogoutResponse.Types.Result.InvalidTimestamp => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server rejected logout due to timestamp mismatch")),
            LogoutResponse.Types.Result.InvalidHmac => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.CryptographicOperationFailed("Server rejected logout due to invalid HMAC")),
            LogoutResponse.Types.Result.Failed => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server failed to complete logout")),
            _ => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server returned unknown logout status"))
        };
    }

    private async Task CompleteLogoutWithCleanupAsync(string membershipId, LogoutReason reason, uint connectId, CancellationToken cancellationToken)
    {
        Result<Unit, Exception> cleanupResult =
            await stateCleanupService.CleanupMembershipStateAsync(membershipId, connectId).ConfigureAwait(false);
        if (cleanupResult.IsErr)
        {
            Log.Warning("[LOGOUT] Cleanup failed during logout. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, cleanupResult.UnwrapErr().Message);
        }

        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);

        await messageBus.PublishAsync(new MembershipLoggedOutEvent(membershipId, reason.ToString()), cancellationToken)
            .ConfigureAwait(false);

        await router.NavigateToAuthenticationAsync().ConfigureAwait(false);
    }

    private async Task<Option<string>> GetCurrentMembershipIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return Option<string>.None;
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        return settings.Membership?.UniqueIdentifier == null
            ? Option<string>.None
            : Option<string>.Some(Helpers.FromByteStringToGuid(settings.Membership.UniqueIdentifier).ToString());
    }

}
