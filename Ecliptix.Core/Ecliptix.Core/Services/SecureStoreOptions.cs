namespace Ecliptix.Core.Services;

public class SecureStoreOptions
{
    public string EncryptedStatePath { get; set; } = "%APPDATA%/Ecliptix/Storage/state";
}