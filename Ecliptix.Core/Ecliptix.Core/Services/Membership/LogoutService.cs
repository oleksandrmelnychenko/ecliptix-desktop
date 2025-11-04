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

    private readonly PendingLogoutProcessor _pendingLogoutProcessor =
        new(networkProvider, new PendingLogoutRequestStorage(applicationSecureStorageProvider));

    private const bool KEEP_PENDING_LOGOUT = true;
    private const bool CLEAR_PENDING_LOGOUT = false;

    public async Task<Result<Unit, LogoutFailure>> LogoutAsync(LogoutReason reason,
        CancellationToken cancellationToken = default)
    {
        Result<string, LogoutFailure> membershipResult = await ValidateAndGetMembershipAsync().ConfigureAwait(false);
        if (membershipResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(membershipResult.UnwrapErr());
        }

        string membershipId = membershipResult.Unwrap();

        Result<(LogoutRequest request, uint connectId), LogoutFailure> prepareResult =
            await PrepareLogoutRequestAsync(membershipId, reason).ConfigureAwait(false);

        if (prepareResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(prepareResult.UnwrapErr());
        }

        (LogoutRequest logoutRequest, uint connectId) = prepareResult.Unwrap();

        Result<LogoutResponse, LogoutFailure> logoutResult =
            await ExecuteServerLogoutAsync(logoutRequest, connectId, cancellationToken).ConfigureAwait(false);

        if (logoutResult.IsErr)
        {
            return await HandleFailedLogoutAsync(logoutRequest, membershipId, reason, connectId,
                cancellationToken).ConfigureAwait(false);
        }

        LogoutResponse response = logoutResult.Unwrap();

        return await ProcessSuccessfulLogoutAsync(response, membershipId, reason, connectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<bool> HasRevocationProofAsync(IApplicationSecureStorageProvider storageProvider,
        string membershipId)
    {
        return await LogoutProofHandler.HasRevocationProofAsync(storageProvider, membershipId);
    }

    private async Task TryStorePendingLogoutAsync(LogoutRequest request)
    {
        Result<Unit, LogoutFailure> storeResult =
            await _pendingLogoutRequestStorage.StorePendingLogoutAsync(request).ConfigureAwait(false);

        if (storeResult.IsErr)
        {
            Log.Warning("[LOGOUT] Failed to store pending logout request: {ERROR}",
                storeResult.UnwrapErr().Message);
        }
    }

    private async Task<Result<Unit, LogoutFailure>> HandleFailedLogoutAsync(
        LogoutRequest logoutRequest,
        string membershipId,
        LogoutReason reason,
        uint connectId,
        CancellationToken cancellationToken)
    {
        await TryStorePendingLogoutAsync(logoutRequest).ConfigureAwait(false);

        await CompleteLogoutWithCleanupAsync(membershipId, reason, connectId, KEEP_PENDING_LOGOUT, cancellationToken)
            .ConfigureAwait(false);

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    private async Task<Result<string, LogoutFailure>> ValidateAndGetMembershipAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return Result<string, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        if (settings.Membership?.UniqueIdentifier == null)
        {
            return Result<string, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        string membershipId = Helpers.FromByteStringToGuid(settings.Membership.UniqueIdentifier).ToString();
        return Result<string, LogoutFailure>.Ok(membershipId);
    }

    private async Task<Result<(LogoutRequest request, uint connectId), LogoutFailure>> PrepareLogoutRequestAsync(
        string membershipId,
        LogoutReason reason)
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return Result<(LogoutRequest, uint), LogoutFailure>.Err(
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

        Result<Unit, LogoutFailure> hmacResult =
            await _logoutProofHandler.GenerateLogoutHmacProofAsync(logoutRequest, membershipId);

        if (hmacResult.IsErr)
        {
            Log.Warning("[LOGOUT] Failed to generate HMAC proof: {ERROR}", hmacResult.UnwrapErr().Message);
            return Result<(LogoutRequest, uint), LogoutFailure>.Err(hmacResult.UnwrapErr());
        }

        uint connectId = NetworkProvider.ComputeUniqueConnectId(
            settings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        return Result<(LogoutRequest, uint), LogoutFailure>.Ok((logoutRequest, connectId));
    }

    private async Task<Result<LogoutResponse, LogoutFailure>> ExecuteServerLogoutAsync(
        LogoutRequest logoutRequest,
        uint connectId,
        CancellationToken cancellationToken)
    {
        TaskCompletionSource<Result<LogoutResponse, LogoutFailure>> responseCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.Logout,
            logoutRequest.ToByteArray(),
            responsePayload =>
            {
                LogoutResponse logoutResponse = LogoutResponse.Parser.ParseFrom(responsePayload);

                if (logoutResponse.Result != LogoutResponse.Types.Result.Succeeded)
                {
                    Log.Warning("[LOGOUT] Server returned non-success status: {Status}", logoutResponse.Result);
                }

                responseCompletionSource.TrySetResult(MapLogoutResponse(logoutResponse));
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            },
            allowDuplicates: false,
            token: cancellationToken,
            waitForRecovery: false).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            NetworkFailure failure = networkResult.UnwrapErr();
            Log.Warning("[LOGOUT] Network request failed: {ERROR}", failure.Message);
            return Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Network request failed", new Exception(failure.Message)));
        }

        Result<LogoutResponse, LogoutFailure> responseResult =
            await responseCompletionSource.Task.ConfigureAwait(false);

        if (responseResult.IsErr)
        {
            LogoutFailure serverFailure = responseResult.UnwrapErr();
            Log.Warning("[LOGOUT] Server returned error: {ERROR}", serverFailure.Message);
            return Result<LogoutResponse, LogoutFailure>.Err(serverFailure);
        }

        return Result<LogoutResponse, LogoutFailure>.Ok(responseResult.Unwrap());
    }

    private async Task<Result<Unit, LogoutFailure>> ProcessSuccessfulLogoutAsync(
        LogoutResponse response,
        string membershipId,
        LogoutReason reason,
        uint connectId,
        CancellationToken cancellationToken)
    {
        Result<Unit, LogoutFailure> proofVerification =
            await _logoutProofHandler.VerifyRevocationProofAsync(response, membershipId, connectId);

        if (proofVerification.IsErr)
        {
            Log.Error("[LOGOUT] Revocation proof verification failed for MembershipId: {MembershipId}",
                membershipId);
        }

        await CompleteLogoutWithCleanupAsync(membershipId, reason, connectId, CLEAR_PENDING_LOGOUT, cancellationToken)
            .ConfigureAwait(false);

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    private async Task FinalizeLogoutAsync(string membershipId, LogoutReason reason, bool shouldRetryPendingLogout,
        CancellationToken cancellationToken)
    {
        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);

        await messageBus.PublishAsync(new MembershipLoggedOutEvent(membershipId, reason.ToString()), cancellationToken)
            .ConfigureAwait(false);

        await router.NavigateToAuthenticationAsync().ConfigureAwait(false);

        if (shouldRetryPendingLogout)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
                        await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync()
                            .ConfigureAwait(false);

                    if (settingsResult.IsOk)
                    {
                        ApplicationInstanceSettings settings = settingsResult.Unwrap();
                        uint anonymousConnectId = NetworkProvider.ComputeUniqueConnectId(
                            settings,
                            PubKeyExchangeType.DataCenterEphemeralConnect);

                        await _pendingLogoutProcessor.ProcessPendingLogoutAsync(anonymousConnectId)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Warning(
                            "[LOGOUT-RETRY] Failed to get settings for immediate retry: {ERROR}, will retry on next startup",
                            settingsResult.UnwrapErr().Message);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[LOGOUT-RETRY] Immediate pending logout retry failed, will retry on next startup");
                }
            }).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Log.Error(task.Exception, "[LOGOUT-RETRY] Unhandled exception in pending logout retry");
                    }
                },
                TaskScheduler.Default);
        }
    }

    private static Result<LogoutResponse, LogoutFailure> MapLogoutResponse(LogoutResponse response)
    {
        return response.Result switch
        {
            LogoutResponse.Types.Result.Succeeded => Result<LogoutResponse, LogoutFailure>.Ok(response),
            LogoutResponse.Types.Result.AlreadyLoggedOut => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.AlreadyLoggedOut("Session is already logged out on the server")),
            LogoutResponse.Types.Result.SessionNotFound => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.SESSION_NOT_FOUND("Active session was not found on the server")),
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

    private async Task CompleteLogoutWithCleanupAsync(string membershipId, LogoutReason reason, uint connectId,
        bool keepPendingLogout, CancellationToken cancellationToken)
    {
        Result<Unit, Exception> cleanupResult =
            await stateCleanupService.CleanupMembershipStateWithKeysAsync(membershipId, connectId)
                .ConfigureAwait(false);
        if (cleanupResult.IsErr)
        {
            Log.Warning("[LOGOUT-CLEANUP] State cleanup with keys failed. MembershipId: {MembershipId}, ERROR: {ERROR}",
                membershipId, cleanupResult.UnwrapErr().Message);
        }

        LogoutProofHandler.ClearRevocationProof(applicationSecureStorageProvider, membershipId);

        if (!keepPendingLogout)
        {
            _pendingLogoutRequestStorage.CLEAR_PENDING_LOGOUT();
        }

        await FinalizeLogoutAsync(membershipId, reason, keepPendingLogout, cancellationToken).ConfigureAwait(false);
    }
}
