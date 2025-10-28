using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Membership;
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

            if (getResult.IsErr || !getResult.Unwrap().HasValue)
            {
                return;
            }

            LogoutRequest pendingRequest = getResult.Unwrap().Value!;

            Guid membershipGuid = Helpers.FromByteStringToGuid(pendingRequest.MembershipIdentifier);
            string membershipId = membershipGuid.ToString();

            Log.Information("[PENDING-LOGOUT-RETRY] Processing pending logout request for ConnectId: {ConnectId}",
                connectId);

            TaskCompletionSource<bool> responseCompletionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.AnonymousLogout,
                pendingRequest.ToByteArray(),
                async responsePayload =>
                {
                    try
                    {
                        AnonymousLogoutResponse logoutResponse = AnonymousLogoutResponse.Parser.ParseFrom(responsePayload);
                        Log.Information("[PENDING-LOGOUT-RETRY] Pending logout request completed with status: {Status}",
                            logoutResponse.Result);

                        if (logoutResponse.Result == AnonymousLogoutResponse.Types.Result.Succeeded)
                        {
                            Log.Information(
                                "[PENDING-LOGOUT-RETRY] Anonymous logout completed successfully for MembershipId: {MembershipId}",
                                membershipId);
                            responseCompletionSource.TrySetResult(true);
                        }
                        else
                        {
                            Log.Warning(
                                "[PENDING-LOGOUT-RETRY] Anonymous logout failed with status: {Status}, Message: {Message}",
                                logoutResponse.Result, logoutResponse.Message ?? "");
                            responseCompletionSource.TrySetResult(true);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[PENDING-LOGOUT-RETRY] Failed to parse pending logout response");
                        responseCompletionSource.TrySetResult(false);
                    }

                    return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                },
                allowDuplicates: false,
                token: cancellationToken).ConfigureAwait(false);

            if (networkResult.IsOk)
            {
                await responseCompletionSource.Task.ConfigureAwait(false);
                pendingLogoutStorage.ClearPendingLogout();
                Log.Information("[PENDING-LOGOUT-RETRY] Successfully processed pending logout");
            }
            else
            {
                Log.Warning("[PENDING-LOGOUT-RETRY] Failed to send pending logout request: {Error}",
                    networkResult.UnwrapErr().Message);
                pendingLogoutStorage.ClearPendingLogout();
                Log.Warning("[PENDING-LOGOUT-RETRY] Deleting corrupted expired pending logout");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PENDING-LOGOUT-RETRY] Unexpected error processing pending logout");
        }
    }
}
