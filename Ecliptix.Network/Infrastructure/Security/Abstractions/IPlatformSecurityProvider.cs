using Ecliptix.Utilities;

namespace Ecliptix.Network.Infrastructure.Security.Abstractions;

public interface IPlatformSecurityProvider : IDisposable
{
    Task<byte[]> GenerateSecureRandomAsync(int length);

    Task StoreKeyInKeychainAsync(string identifier, byte[] key);

    Task<byte[]?> GetKeyFromKeychainAsync(string identifier);

    Task DeleteKeyFromKeychainAsync(string identifier);

    Task<byte[]> GetOrCreateHmacKeyAsync();

    bool IsHardwareSecurityAvailable();

    Task<Option<byte[]>> HardwareEncryptAsync(byte[] data);

    Task<Option<byte[]>> HardwareDecryptAsync(byte[] data);
}
