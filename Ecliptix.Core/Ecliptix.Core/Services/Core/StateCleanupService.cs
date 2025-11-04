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

internal sealed class StateCleanupService(
    IIdentityService identityService,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    NetworkProvider networkProvider) : IStateCleanupService
{
    public async Task<Result<Unit, Exception>> CleanupMembershipStateAsync(string membershipId, uint connectId)
    {
        try
        {
            Result<Unit, SecureStorageFailure> deleteResult = await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

            if (deleteResult.IsErr)
            {
                Log.Warning("[STATE-CLEANUP-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, ERROR: {ERROR}",
                    connectId, deleteResult.UnwrapErr().Message);
            }

            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null).ConfigureAwait(false);

            networkProvider.ClearConnection(connectId);

            return Result<Unit, Exception>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[STATE-CLEANUP] Cleanup failed for MembershipId: {MembershipId}", membershipId);
            return Result<Unit, Exception>.Err(ex);
        }
    }

    public async Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string membershipId, uint connectId)
    {
        try
        {
            Result<Unit, SecureStorageFailure> deleteResult = await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString()).ConfigureAwait(false);

            if (deleteResult.IsErr)
            {
                Log.Warning("[STATE-CLEANUP-FULL-DELETE] Failed to delete protocol state file for ConnectId: {ConnectId}, ERROR: {ERROR}",
                    connectId, deleteResult.UnwrapErr().Message);
            }

            Result<Unit, Ecliptix.Utilities.Failures.Authentication.AuthenticationFailure> clearResult =
                await identityService.ClearAllCacheAsync(membershipId).ConfigureAwait(false);

            if (clearResult.IsErr)
            {
                Log.Warning("[STATE-CLEANUP-FULL] Identity cache clear failed: {ERROR}",
                    clearResult.UnwrapErr().Message);
            }

            await applicationSecureStorageProvider.SetApplicationMembershipAsync(null).ConfigureAwait(false);

            networkProvider.ClearConnection(connectId);

            return Result<Unit, Exception>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[STATE-CLEANUP-FULL] Full cleanup failed for MembershipId: {MembershipId}", membershipId);
            return Result<Unit, Exception>.Err(ex);
        }
    }
}
