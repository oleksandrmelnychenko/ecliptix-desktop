using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface ISecureKeySplitter
{
    Task<Result<KeySplitResult, string>> SplitKeyAsync(byte[] key, int threshold = 3, int totalShares = 5, byte[]? hmacKey = null);

    Task<Result<byte[], string>> ReconstructKeyAsync(KeyShare[] shares, byte[]? hmacKey = null);

    bool ValidateShares(KeyShare[] shares, byte[]? hmacKey = null);

    Task<Result<Unit, string>> SecurelyDisposeSharesAsync(KeyShare[] shares);
}