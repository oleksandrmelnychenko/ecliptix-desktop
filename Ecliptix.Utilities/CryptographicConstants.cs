namespace Ecliptix.Utilities;

public static class CryptographicConstants
{
    public const int SHA_256_HASH_SIZE = 32;
    public const int BLAKE_2_B_SALT_SIZE = 16;
    public const int BLAKE_2_B_PERSONAL_SIZE = 16;
    public const int GUID_BYTE_LENGTH = 16;
    public const int AES_KEY_SIZE = 32;
    public const int AES_IV_SIZE = 16;
    public const int HASH_FINGERPRINT_LENGTH = 16;

    public static class Argon2
    {
        public const int DEFAULT_ITERATIONS = 4;
        public const int DEFAULT_MEMORY_SIZE = 262144;
        public const int DEFAULT_PARALLELISM = 4;
        public const int DEFAULT_OUTPUT_LENGTH = 64;
    }

    public static class Buffer
    {
        public const int MAX_INFO_SIZE = 128;
        public const int MAX_PREVIOUS_BLOCK_SIZE = 64;
        public const int MAX_ROUND_SIZE = 64;
    }

    public static class KeyDerivation
    {
        public const int ADDITIONAL_ROUNDS_COUNT = 3;
        public const string ROUND_KEY_FORMAT = "round-{0}";
    }
}
