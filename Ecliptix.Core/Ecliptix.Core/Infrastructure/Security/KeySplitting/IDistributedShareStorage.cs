using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IDistributedShareStorage
{
    Task<Result<Unit, KeySplittingFailure>> StoreKeySharesAsync(KeySplitResult splitKeys, Guid membershipId);
    Task<Result<KeyShare[], KeySplittingFailure>> RetrieveKeySharesAsync(Guid membershipId, int minimumShares = 3);
    Task<Result<Unit, KeySplittingFailure>> RemoveKeySharesAsync(Guid membershipId);
    Task<Result<bool, KeySplittingFailure>> HasStoredSharesAsync(Guid membershipId);
    Task<Result<Unit, KeySplittingFailure>> ClearAllCacheAsync();
}