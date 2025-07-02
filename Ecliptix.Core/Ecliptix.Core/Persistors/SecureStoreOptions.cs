namespace Ecliptix.Core.Persistors;

public class SecureStoreOptions
{
    public string EncryptedStatePath { get; set; } = "%APPDATA%/Ecliptix/Storage/state";
}