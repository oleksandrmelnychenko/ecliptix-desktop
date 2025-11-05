using System.Threading.Tasks;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IHardenedKeyDerivation
{
    Task<Result<SodiumSecureMemoryHandle, KeySplittingFailure>> DeriveEnhancedMasterKeyHandleAsync(
        SodiumSecureMemoryHandle baseKeyHandle,
        string context,
        KeyDerivationOptions options);
}

public class KeyDerivationOptions
{
    public int MEMORY_SIZE { get; set; } = 262144;
    public int ITERATIONS { get; set; } = 4;
    public int DegreeOfParallelism { get; set; } = 4;
    public bool UseHardwareEntropy { get; set; } = true;
    public int OutputLength { get; set; } = 64;
}
