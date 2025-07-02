using System;

namespace Ecliptix.Core.Services;

public class SecureStoreOptions
{
    public string EncryptedStatePath { get; set; } = "%APPDATA%/Ecliptix/Storage/state";

    public void Validate()
    {
        if (string.IsNullOrEmpty(EncryptedStatePath))
        {
            throw new InvalidOperationException("EncryptedStatePath must be specified.");
        }
    }
}