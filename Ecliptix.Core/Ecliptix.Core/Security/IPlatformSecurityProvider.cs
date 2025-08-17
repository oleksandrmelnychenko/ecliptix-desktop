using System.Threading.Tasks;

namespace Ecliptix.Core.Security;

public interface IPlatformSecurityProvider
{
    Task<byte[]> GenerateSecureRandomAsync(int length);

    Task StoreKeyInKeychainAsync(string identifier, byte[] key);

    Task<byte[]?> GetKeyFromKeychainAsync(string identifier);

    Task DeleteKeyFromKeychainAsync(string identifier);

    Task<byte[]> GetOrCreateHmacKeyAsync();

    bool IsHardwareSecurityAvailable();

    Task<byte[]> HardwareEncryptAsync(byte[] data);

    Task<byte[]> HardwareDecryptAsync(byte[] data);
}