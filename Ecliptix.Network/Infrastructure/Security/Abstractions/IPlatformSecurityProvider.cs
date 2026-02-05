using Ecliptix.Utilities;

namespace Ecliptix.Network.Infrastructure.Security.Abstractions;

public interface IPlatformSecurityProvider : IDisposable
{
    Task<byte[]> GenerateSecureRandomAsync(int length);

    Task StoreKeyInKeychainAsync(string identifier, byte[] key);

    Task<byte[]?> GetKeyFromKeychainAsync(string identifier);

    Task DeleteKeyFromKeychainAsync(string identifier);

    Task<byte[]> GetOrCreateHmacKeyAsync();

    /// <summary>
    /// Gets or creates a 32-byte encryption key for sealing native protocol session state.
    /// The key is derived from the platform HMAC key using HKDF-SHA256.
    /// </summary>
    Task<byte[]> GetOrCreateSessionStateKeyAsync();

    bool IsHardwareSecurityAvailable();

    Task<Option<byte[]>> HardwareEncryptAsync(byte[] data);

    Task<Option<byte[]>> HardwareDecryptAsync(byte[] data);
}
