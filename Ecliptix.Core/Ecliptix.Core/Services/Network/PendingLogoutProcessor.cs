using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
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
    PendingLogoutRequestStorage pendingLogoutStorage,
    IIdentityService identityService,
    IApplicationSecureStorageProvider secureStorageProvider)
{

    private readonly LogoutProofHandler _logoutProofHandler = new(identityService, secureStorageProvider);
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

            Log.Information("[PENDING-LOGOUT-RETRY] Processing pending logout request for ConnectId: {ConnectId}", connectId);

            TaskCompletionSource<bool> responseCompletionSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.Logout,
                pendingRequest.ToByteArray(),
                async responsePayload =>
                {
                    try
                    {
                        LogoutResponse logoutResponse = LogoutResponse.Parser.ParseFrom(responsePayload);
                        Log.Information("[PENDING-LOGOUT-RETRY] Pending logout request completed with status: {Status}",
                            logoutResponse.Result);
                        responseCompletionSource.TrySetResult(true);

                        if (logoutResponse.Result == LogoutResponse.Types.Result.Succeeded)
                        {

                                try
                                {
                                    Result<Unit, LogoutFailure> proofVerification =
                                        await _logoutProofHandler.VerifyRevocationProofAsync(logoutResponse, membershipId, connectId);

                                    if (proofVerification.IsErr)
                                    {
                                        Log.Error("[PENDING-LOGOUT-RETRY] Revocation proof verification failed for MembershipId: {MembershipId}, Error: {Error}",
                                            membershipId, proofVerification.UnwrapErr().Message);
                                    }
                                    else
                                    {
                                        Log.Information("[PENDING-LOGOUT-RETRY] Revocation proof verified successfully for MembershipId: {MembershipId}",
                                            membershipId);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, "[PENDING-LOGOUT-RETRY] Unexpected error during revocation proof verification for MembershipId: {MembershipId}",
                                        membershipId);
                                }

                                responseCompletionSource.TrySetResult(true);
                        }
                        else
                        {
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
