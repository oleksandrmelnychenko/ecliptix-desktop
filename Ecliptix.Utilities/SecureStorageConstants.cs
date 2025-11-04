namespace Ecliptix.Utilities;

internal static class SecureStorageConstants
{
    internal static class Encryption
    {
        public const int SALT_SIZE = 32;
        public const int NONCE_SIZE = 12;
        public const int TAG_SIZE = 16;
        public const int KEY_SIZE = 32;
        public const int HMAC_SHA_512_SIZE = 64;
    }

    internal static class Argon2
    {
        public const int ITERATIONS = 4;
        public const int MEMORY_SIZE = 131072;
        public const int PARALLELISM = 4;
    }

    internal static class Header
    {
        public const string MAGIC_HEADER = "ECLIPTIX_SECURE_V1";
        public const int CURRENT_VERSION = 1;
    }

    internal static class Identity
    {
        public const string MASTER_KEY_STORAGE_PREFIX = "master_";
        public const string KEYCHAIN_WRAP_KEY_PREFIX = "ecliptix_master_wrap_";
        public const string REVOCATION_PROOF_PREFIX = "revocation_proof_";
        public const int AES_KEY_SIZE = 32;
        public const int AES_IV_SIZE = 16;
    }
}
