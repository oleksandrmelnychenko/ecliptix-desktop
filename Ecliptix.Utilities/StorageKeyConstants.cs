namespace Ecliptix.Utilities;

public static class StorageKeyConstants
{
    public static class MasterKey
    {
        public const string StoragePrefix = "master_";
        public const string KeychainWrapPrefix = "ecliptix_master_wrap_";
    }

    public static class Share
    {
        public const string HardwarePrefix = "hw_share_";
        public const string KeychainPrefix = "kc_share_";
        public const string LocalPrefix = "local_share_";
        public const string BackupPrefix = "backup_";
        public const string MemoryPrefix = "mem";
        public const string EcliptixSharePrefix = "ecliptix_share_";
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
        public const string ProtocolRootKey = "ecliptix-protocol-root-key";
    }

    public static class SemanticOperation
    {
        public const string AuthSignInPrefix = "auth:signin:";
        public const string AuthSignUpPrefix = "auth:signup:";
        public const string StreamPrefix = "stream:";
        public const string DataPrefix = "data:";
    }
}
