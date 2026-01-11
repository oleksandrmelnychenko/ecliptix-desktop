namespace Ecliptix.Core.Data.SecureStorage.Configuration;

public sealed class SecureStoreOptions
{
    public string EncryptedStatePath { get; init; } = "%APPDATA%/Ecliptix/Storage/state";
}
