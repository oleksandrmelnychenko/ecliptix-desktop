namespace Ecliptix.Core.Infrastructure.Data.SecureStorage.Configuration;

public sealed class SecureStoreOptions
{
    public string ENCRYPTED_STATE_PATH { get; set; } = "%APPDATA%/Ecliptix/Storage/state";
}
