using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Events;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Network.Infrastructure.Data;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Ecliptix.Network.Services.Common;
using Ecliptix.Network.Services.Network;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Shell.Services.Membership;

internal sealed class LogoutService(
    NetworkProvider networkProvider,
    IMessageBus messageBus,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IApplicationStateManager stateManager,
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

        Result<string, LogoutFailure> accountResult = await ValidateAndGetAccountAsync().ConfigureAwait(false);
        if (accountResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(accountResult.UnwrapErr());
        }

        string accountId = accountResult.Unwrap();

        Result<(LogoutRequest request, uint connectId), LogoutFailure> prepareResult =
            await PrepareLogoutRequestAsync(membershipId, accountId, reason).ConfigureAwait(false);

        if (prepareResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(prepareResult.UnwrapErr());
        }

        (LogoutRequest logoutRequest, uint connectId) = prepareResult.Unwrap();

        Result<LogoutResponse, LogoutFailure> logoutResult =
            await ExecuteServerLogoutAsync(logoutRequest, connectId, cancellationToken).ConfigureAwait(false);

        if (logoutResult.IsErr)
        {
            return await HandleFailedLogoutAsync(logoutRequest, membershipId, accountId, reason, connectId,
                cancellationToken).ConfigureAwait(false);
        }

        LogoutResponse response = logoutResult.Unwrap();

        return await ProcessSuccessfulLogoutAsync(response, membershipId, accountId, reason, connectId, cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<bool> HasRevocationProofAsync(IApplicationSecureStorageProvider storageProvider,
        string membershipId) =>
        await LogoutProofHandler.HasRevocationProofAsync(storageProvider, membershipId);

    private async Task TryStorePendingLogoutAsync(LogoutRequest request) =>
        await _pendingLogoutRequestStorage.StorePendingLogoutAsync(request).ConfigureAwait(false);

    private async Task<Result<Unit, LogoutFailure>> HandleFailedLogoutAsync(
        LogoutRequest logoutRequest,
        string membershipId,
        string accountId,
        LogoutReason reason,
        uint connectId,
        CancellationToken cancellationToken)
    {
        await TryStorePendingLogoutAsync(logoutRequest).ConfigureAwait(false);

        await CompleteLogoutWithCleanupAsync(membershipId, accountId, reason, connectId, KEEP_PENDING_LOGOUT, cancellationToken)
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

        if (settings.Membership?.MembershipId == null)
        {
            return Result<string, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        string membershipId = Helpers.FromByteStringToGuid(settings.Membership.MembershipId).ToString();
        return Result<string, LogoutFailure>.Ok(membershipId);
    }

    private async Task<Result<string, LogoutFailure>> ValidateAndGetAccountAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
        {
            return Result<string, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        if (settings.CurrentAccountId == null || settings.CurrentAccountId.IsEmpty)
        {
            return Result<string, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active account found"));
        }

        string accountId = Helpers.FromByteStringToGuid(settings.CurrentAccountId).ToString();
        return Result<string, LogoutFailure>.Ok(accountId);
    }

    private async Task<Result<(LogoutRequest request, uint connectId), LogoutFailure>> PrepareLogoutRequestAsync(
        string membershipId,
        string accountId,
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
            MembershipId = membershipIdBytes,
            LogoutReason = reason.ToString(),
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Scope = LogoutScope.ThisDevice,
            AccountId = settings.CurrentAccountId
        };

        Result<Unit, LogoutFailure> hmacResult =
            await _logoutProofHandler.GenerateLogoutHmacProofAsync(logoutRequest, membershipId, accountId);

        if (hmacResult.IsErr)
        {
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

                if (logoutResponse.Result != LogoutResponse.Types.Result.LogoutResultSucceeded)
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
            return Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Network request failed", new Exception(failure.Message)));
        }

        Result<LogoutResponse, LogoutFailure> responseResult =
            await responseCompletionSource.Task.ConfigureAwait(false);

        if (!responseResult.IsErr)
        {
            return Result<LogoutResponse, LogoutFailure>.Ok(responseResult.Unwrap());
        }

        LogoutFailure serverFailure = responseResult.UnwrapErr();
        return Result<LogoutResponse, LogoutFailure>.Err(serverFailure);
    }

    private async Task<Result<Unit, LogoutFailure>> ProcessSuccessfulLogoutAsync(
        LogoutResponse response,
        string membershipId,
        string accountId,
        LogoutReason reason,
        uint connectId,
        CancellationToken cancellationToken)
    {
        // Note: Anonymous logout (LogoutResponse) does not include revocation proof
        // Revocation proof verification is only available for authenticated logout (AuthenticatedLogoutResponse)
        Log.Debug("[LOGOUT] Anonymous logout completed for MembershipId: {MembershipId}", membershipId);

        await CompleteLogoutWithCleanupAsync(membershipId, accountId, reason, connectId, CLEAR_PENDING_LOGOUT, cancellationToken)
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
            }).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Log.Warning(task.Exception,
                            "[LOGOUT-RETRY] Immediate pending logout retry failed, will retry on next startup");
                    }
                },
                TaskScheduler.Default);
        }
    }

    private static Result<LogoutResponse, LogoutFailure> MapLogoutResponse(LogoutResponse response)
    {
        return response.Result switch
        {
            LogoutResponse.Types.Result.LogoutResultSucceeded => Result<LogoutResponse, LogoutFailure>.Ok(response),
            LogoutResponse.Types.Result.LogoutResultAlreadyLoggedOut => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.AlreadyLoggedOut("Session is already logged out on the server")),
            LogoutResponse.Types.Result.LogoutResultSessionNotFound => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.SessionNotFound("Active session was not found on the server")),
            LogoutResponse.Types.Result.LogoutResultInvalidTimestamp => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server rejected logout due to timestamp mismatch")),
            LogoutResponse.Types.Result.LogoutResultInvalidHmac => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.CryptographicOperationFailed("Server rejected logout due to invalid HMAC")),
            LogoutResponse.Types.Result.LogoutResultFailed => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server failed to complete logout")),
            _ => Result<LogoutResponse, LogoutFailure>.Err(
                LogoutFailure.UnexpectedError("Server returned unknown logout status"))
        };
    }

    private async Task CompleteLogoutWithCleanupAsync(string membershipId, string accountId, LogoutReason reason, uint connectId,
        bool keepPendingLogout, CancellationToken cancellationToken)
    {
        await identityService.CleanupMembershipStateWithKeysAsync(accountId, connectId)
            .ConfigureAwait(false);

        networkProvider.ClearConnection(connectId);

        LogoutProofHandler.ClearRevocationProof(applicationSecureStorageProvider, membershipId);

        if (!keepPendingLogout)
        {
            _pendingLogoutRequestStorage.ClearPendingLogout();
        }

        await FinalizeLogoutAsync(membershipId, reason, keepPendingLogout, cancellationToken).ConfigureAwait(false);
    }
}
