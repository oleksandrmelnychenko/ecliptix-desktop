namespace Ecliptix.Utilities;

internal static class StorageKeyConstants
{
    internal static class SessionContext
    {
        public const string SESSION_KEY_PREFIX = "ecliptix-session-key";
        public const string ED_25519_CONTEXT = "ED25519";
        public const string X_25519_CONTEXT = "X25519";
        public const string SIGNED_PRE_KEY_CONTEXT = "SPK_X25519";
    }
}
