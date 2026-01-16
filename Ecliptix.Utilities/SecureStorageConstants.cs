namespace Ecliptix.Utilities;

public static class SecureStorageConstants
{
    public static class Encryption
    {
        public const int NONCE_SIZE = 12;
        public const int TAG_SIZE = 16;
        public const int KEY_SIZE = 32;
        public const int HMAC_SHA_512_SIZE = 64;
    }

    public static class Header
    {
        public const string MAGIC_HEADER = "ECLIPTIX_SECURE_V1";
        public const int CURRENT_VERSION = 2;
    }

    public static class Identity
    {
        public const string MASTER_KEY_SHARE_STORAGE_PREFIX = "master_share_";
        public const string REVOCATION_PROOF_PREFIX = "revocation_proof_";
    }
}
