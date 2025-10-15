namespace Ecliptix.Utilities;

internal static class SecureStorageConstants
{
    internal static class Encryption
    {
        public const int SaltSize = 32;
        public const int NonceSize = 12;
        public const int TagSize = 16;
        public const int KeySize = 32;
        public const int HmacSha512Size = 64;
    }

    internal static class Argon2
    {
        public const int Iterations = 4;
        public const int MemorySize = 131072;
        public const int Parallelism = 4;
    }

    internal static class Header
    {
        public const string MagicHeader = "ECLIPTIX_SECURE_V1";
        public const int CurrentVersion = 1;
    }

    internal static class Identity
    {
        public const string MasterKeyStoragePrefix = "master_";
        public const string KeychainWrapKeyPrefix = "ecliptix_master_wrap_";
        public const int AesKeySize = 32;
        public const int AesIvSize = 16;
    }
}
