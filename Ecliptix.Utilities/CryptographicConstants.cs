namespace Ecliptix.Utilities;

public static class CryptographicConstants
{
    public const int Sha256HashSize = 32;
    public const int Blake2BSaltSize = 16;
    public const int Blake2BPersonalSize = 16;
    public const int GuidByteLength = 16;
    public const int AesKeySize = 32;
    public const int AesIvSize = 16;
    public const int HashFingerprintLength = 16;

    public static class Argon2
    {
        public const int DefaultIterations = 4;
        public const int DefaultMemorySize = 262144;
        public const int DefaultParallelism = 4;
        public const int DefaultOutputLength = 64;
    }

    public static class Buffer
    {
        public const int MaxInfoSize = 128;
        public const int MaxPreviousBlockSize = 64;
        public const int MaxRoundSize = 64;
        public const int SmallStackSize = 256;
        public const int MediumStackSize = 512;
    }

    public static class KeyDerivation
    {
        public const int AdditionalRoundsCount = 3;
        public const string RoundKeyFormat = "round-{0}";
    }
}
