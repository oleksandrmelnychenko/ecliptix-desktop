using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IMultiLocationKeyStorage
{
    // Original methods for backward compatibility (session keys with connectId)
    Task<Result<Unit, string>> StoreKeySharesAsync(KeySplitResult splitKeys, uint connectId);
    Task<Result<KeyShare[], string>> RetrieveKeySharesAsync(uint connectId, int minimumShares = 3);
    Task<Result<Unit, string>> RemoveKeySharesAsync(uint connectId);
    Task<Result<bool, string>> HasStoredSharesAsync(uint connectId);

    // New overloads for persistent storage (master keys with membershipId)
    Task<Result<Unit, string>> StoreKeySharesAsync(KeySplitResult splitKeys, string identifier);
    Task<Result<KeyShare[], string>> RetrieveKeySharesAsync(string identifier, int minimumShares = 3);
    Task<Result<Unit, string>> RemoveKeySharesAsync(string identifier);
    Task<Result<bool, string>> HasStoredSharesAsync(string identifier);

    Task<Result<byte[], string>> StoreAndReconstructKeyAsync(
        byte[] originalKey,
        uint connectId,
        int threshold = 3,
        int totalShares = 5);
}