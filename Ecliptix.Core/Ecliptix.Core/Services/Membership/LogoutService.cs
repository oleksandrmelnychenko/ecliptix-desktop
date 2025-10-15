using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Membership;

public sealed class LogoutService(
    NetworkProvider networkProvider,
    IUnifiedMessageBus messageBus,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IApplicationStateManager stateManager,
    IStateCleanupService stateCleanupService,
    IApplicationRouter router)
    : ILogoutService
{
    public async Task<Result<Unit, LogoutFailure>> LogoutAsync(LogoutReason reason, CancellationToken ct = default)
    {
        string? membershipId = await GetCurrentMembershipIdAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(membershipId))
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

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
        byte[] membershipIdBytes = Guid.Parse(membershipId).ToByteArray();

        LogoutRequest logoutRequest = new()
        {
            MembershipIdentifier = ByteString.CopyFrom(membershipIdBytes),
            LogoutReason = reason.ToString(),
            Timestamp = timestamp,
            Scope = LogoutScope.ThisDevice
        };

        uint connectId = NetworkProvider.ComputeUniqueConnectId(
            settings,
            PubKeyExchangeType.DataCenterEphemeralConnect);

        Log.Information("[LOGOUT] Using existing authenticated protocol. ConnectId: {ConnectId}", connectId);

        TaskCompletionSource<Result<LogoutResponse, LogoutFailure>> responseCompletionSource = new();

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

                    responseCompletionSource.TrySetResult(Result<LogoutResponse, LogoutFailure>.Ok(logoutResponse));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[LOGOUT] Failed to parse logout response");
                    responseCompletionSource.TrySetResult(
                        Result<LogoutResponse, LogoutFailure>.Err(
                            LogoutFailure.NetworkRequestFailed("Failed to parse logout response", ex)));
                }
                return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            },
            allowDuplicates: false,
            token: ct).ConfigureAwait(false);

        if (networkResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Logout network request failed",
                    new Exception(networkResult.UnwrapErr().Message)));
        }

        Result<LogoutResponse, LogoutFailure> logoutResult = await responseCompletionSource.Task.ConfigureAwait(false);

        if (logoutResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(logoutResult.UnwrapErr());
        }

        Log.Information("[LOGOUT] Logout API call succeeded. Starting cleanup for MembershipId: {MembershipId}",
            membershipId);

        Result<Unit, Exception> cleanupResult = await stateCleanupService.CleanupUserStateAsync(membershipId, connectId).ConfigureAwait(false);
        if (cleanupResult.IsErr)
        {
            Log.Warning("[LOGOUT-CLEANUP] Failed to cleanup user state during logout. MembershipId: {MembershipId}, Error: {Error}",
                membershipId, cleanupResult.UnwrapErr().Message);
        }

        await stateManager.TransitionToAnonymousAsync().ConfigureAwait(false);

        await messageBus.PublishAsync(new MembershipLoggedOutEvent(membershipId, reason.ToString()), ct).ConfigureAwait(false);

        await router.NavigateToAuthenticationAsync().ConfigureAwait(false);

        Log.Information("[LOGOUT] Logout completed successfully. MembershipId: {MembershipId}", membershipId);

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    private async Task<string?> GetCurrentMembershipIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync().ConfigureAwait(false);

        if (settingsResult.IsErr)
            return null;

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        if (settings.Membership?.UniqueIdentifier == null)
            return null;

        return SecureByteStringInterop.WithByteStringAsSpan(settings.Membership.UniqueIdentifier,
            span => new Guid(span.ToArray()).ToString());
    }
}
