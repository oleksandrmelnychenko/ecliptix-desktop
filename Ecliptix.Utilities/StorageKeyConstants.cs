namespace Ecliptix.Utilities;

public static class StorageKeyConstants
{
    public static class MasterKey
    {
        public const string StoragePrefix = "master_";
    }

    public static class SessionContext
    {
        public const string SignInSession = "ecliptix-signin-session";
        public const string SessionKeyPrefix = "ecliptix-session-key";
        public const string MasterSalt = "ECLIPTIX_MSTR_V1";
        public const string DomainContext = "ECLIPTIX_MASTER_KEY";
        public const string Ed25519Context = "ED25519";
        public const string X25519Context = "X25519";
        public const string SignedPreKeyContext = "SPK_X25519";
    }
}
