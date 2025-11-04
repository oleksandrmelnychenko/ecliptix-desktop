using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Network;

internal sealed class PendingLogoutProcessor(
    NetworkProvider networkProvider,
    PendingLogoutRequestStorage pendingLogoutStorage)
{
    public async Task ProcessPendingLogoutAsync(
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Result<Option<LogoutRequest>, LogoutFailure> getResult =
                await pendingLogoutStorage.GetPendingLogoutAsync().ConfigureAwait(false);

            if (getResult.IsErr || !getResult.Unwrap().IsSome)
            {
                return;
            }

            LogoutRequest pendingRequest = getResult.Unwrap().Value!;

            TaskCompletionSource<bool> responseCompletionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.AnonymousLogout,
                pendingRequest.ToByteArray(),
                async responsePayload =>
                {
                    // Parse response to validate format, but don't inspect contents
                    // Complete the response regardless of logout result (both success and failure handled the same)
                    AnonymousLogoutResponse.Parser.ParseFrom(responsePayload);
                    responseCompletionSource.TrySetResult(true);

                    return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                },
                allowDuplicates: false,
                token: cancellationToken).ConfigureAwait(false);

            if (networkResult.IsOk)
            {
                await responseCompletionSource.Task.ConfigureAwait(false);
                pendingLogoutStorage.CLEAR_PENDING_LOGOUT();
            }
            else
            {
                Log.Warning("[PENDING-LOGOUT-RETRY] Failed to send pending logout request: {ERROR}. Deleting corrupted expired pending logout",
                    networkResult.UnwrapErr().Message);
                pendingLogoutStorage.CLEAR_PENDING_LOGOUT();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await EnsureProtocolInBackground();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[VERIFY-OTP] Background secrecy channel establishment failed");
                    }
                }).ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted && task.Exception != null)
                        {
                            Log.Error(task.Exception, "[PENDING-LOGOUT-RETRY] Unhandled exception ensuring protocol");
                        }
                    },
                    TaskScheduler.Default);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PENDING-LOGOUT-RETRY] Unexpected error processing pending logout");
        }
    }

    private async Task EnsureProtocolInBackground()
    {
        Result<uint, NetworkFailure> ensureResult = await networkProvider.EnsureProtocolForTypeAsync(
            PubKeyExchangeType.DataCenterEphemeralConnect);

        if (ensureResult.IsErr)
        {
            Log.Error("[VERIFY-OTP] Failed to ensure protocol: {ERROR}",
                ensureResult.UnwrapErr().Message);
        }
    }
}
