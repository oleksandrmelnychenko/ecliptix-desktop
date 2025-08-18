namespace Ecliptix.Core.Infrastructure.Data.SecureStorage.Configuration;

public class SecureStoreOptions
{
    public string EncryptedStatePath { get; set; } = "%APPDATA%/Ecliptix/Storage/state";
}