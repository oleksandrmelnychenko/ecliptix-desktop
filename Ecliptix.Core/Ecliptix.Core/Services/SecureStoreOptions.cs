namespace Ecliptix.Core.Services;

/// <summary>
/// Provides configuration options for the SecureByteStorageService.
/// This allows specifying paths and authentication methods for the secure store.
/// </summary>
public class SecureStoreOptions
{
    /// <summary>
    /// Gets or sets the full path to the encrypted data store file.
    /// Default is "secure_data.bin" in the application's base directory.
    /// </summary>
    public string StorePath { get; set; } = "secure_data.bin";

    /// <summary>
    /// Gets or sets the full path to the key file used to unlock the data store.
    /// This is ignored if UsePassword is set to true.
    /// Default is "secure_data.key" in the application's base directory.
    /// </summary>
    public string KeyPath { get; set; } = "secure_data.key";

    /// <summary>
    /// Gets or sets the password used to unlock the data store.
    /// This is only used if UsePassword is set to true.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether to use password-based encryption.
    /// If false (default), key file-based encryption is used.
    /// </summary>
    public bool UsePassword { get; set; } = false;
}