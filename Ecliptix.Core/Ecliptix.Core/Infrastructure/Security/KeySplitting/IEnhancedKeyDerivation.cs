using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IEnhancedKeyDerivation
{
    Task<Result<byte[], string>> DeriveEnhancedKeyAsync(
        byte[] baseKey,
        string context,
        uint connectId,
        KeyDerivationOptions? options = null);

    Task<Result<byte[], string>> StretchKeyAsync(
        byte[] input,
        byte[] salt,
        int outputLength,
        KeyDerivationOptions? options = null);

    byte[] GenerateContextSalt(string context, uint connectId);
}

public class KeyDerivationOptions
{
    public int MemorySize { get; set; } = 262144; // 256MB
    public int Iterations { get; set; } = 4;
    public int DegreeOfParallelism { get; set; } = 4;
    public bool UseHardwareEntropy { get; set; } = true;
    public int OutputLength { get; set; } = 64;
}