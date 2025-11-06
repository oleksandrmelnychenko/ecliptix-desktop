namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IHardenedKeyDerivation;

public class KeyDerivationOptions
{
    public int MemorySize { get; set; } = 262144;
    public int Iterations { get; set; } = 4;
    public int DegreeOfParallelism { get; set; } = 4;
    public bool UseHardwareEntropy { get; set; } = true;
    public int OutputLength { get; set; } = 64;
}
