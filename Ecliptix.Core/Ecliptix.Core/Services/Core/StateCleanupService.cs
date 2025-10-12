using System;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Utilities;
using Serilog;

namespace Ecliptix.Core.Services.Core;

public class StateCleanupService(
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    NetworkProvider networkProvider) : IStateCleanupService
{
    public async Task<Result<Unit, Exception>> CleanupUserStateAsync(string membershipId, uint connectId)
    {
        try
        {
            Log.Information("[STATE-CLEANUP] Starting lightweight cleanup for MembershipId: {MembershipId}, ConnectId: {ConnectId}",
                membershipId, connectId);

            Log.Information("[STATE-CLEANUP-DELETE] Deleting protocol state file for ConnectId: {ConnectId}", connectId);
            Result<Unit, SecureStorageFailure> deleteResult = await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

            if (deleteResult.IsOk)
            {
                Log.Information("[STATE-CLEANUP-DELETE] Protocol state file deleted successfully for ConnectId: {ConnectId}", connectId);
            }
            else
            {
                Log.Warning("[STATE-CLEANUP-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, Error: {Error}",
                    connectId, deleteResult.UnwrapErr().Message);
            }

            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null);

            networkProvider.ClearConnection(connectId);

            Log.Information("[STATE-CLEANUP] Lightweight cleanup completed successfully for MembershipId: {MembershipId}",
                membershipId);

            return Result<Unit, Exception>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[STATE-CLEANUP] Cleanup failed for MembershipId: {MembershipId}", membershipId);
            return Result<Unit, Exception>.Err(ex);
        }
    }

    public async Task<Result<Unit, Exception>> CleanupUserStateWithKeysAsync(string membershipId, uint connectId)
    {
        try
        {
            Log.Information("[STATE-CLEANUP-FULL] Starting full cleanup with key deletion for MembershipId: {MembershipId}, ConnectId: {ConnectId}",
                membershipId, connectId);

            Log.Information("[STATE-CLEANUP-FULL-DELETE] Deleting protocol state file for ConnectId: {ConnectId}", connectId);
            Result<Unit, SecureStorageFailure> deleteResult = await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

            if (deleteResult.IsOk)
            {
                Log.Information("[STATE-CLEANUP-FULL-DELETE] Protocol state file deleted successfully for ConnectId: {ConnectId}", connectId);
            }
            else
            {
                Log.Warning("[STATE-CLEANUP-FULL-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, Error: {Error}",
                    connectId, deleteResult.UnwrapErr().Message);
            }

            Result<Unit, Ecliptix.Utilities.Failures.Authentication.AuthenticationFailure> clearResult =
                await identityService.ClearAllCacheAsync(membershipId);

            if (clearResult.IsErr)
            {
                Log.Warning("[STATE-CLEANUP-FULL] Identity cache clear failed: {Error}",
                    clearResult.UnwrapErr().Message);
            }

            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null);

            networkProvider.ClearConnection(connectId);

            Log.Information("[STATE-CLEANUP-FULL] Full cleanup completed successfully for MembershipId: {MembershipId}",
                membershipId);

            return Result<Unit, Exception>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[STATE-CLEANUP-FULL] Full cleanup failed for MembershipId: {MembershipId}", membershipId);
            return Result<Unit, Exception>.Err(ex);
        }
    }
}
