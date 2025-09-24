using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;

namespace Ecliptix.Core.Services.Authentication;

public class SessionKeyService(ISecureProtocolStateStorage storage, IPlatformSecurityProvider platformProvider)
    : ISessionKeyService
{
    private readonly ConcurrentDictionary<uint, byte[]> _sessionKeyCache = new();

    public async Task StoreSessionKeyAsync(byte[] sessionKey, uint connectId)
    {
        try
        {
            byte[] protectedKey = await WrapSessionKeyAsync(sessionKey, connectId);
            await storage.SaveStateAsync(protectedKey, $"session_{connectId}");

            byte[] sessionKeyCopy = new byte[sessionKey.Length];
            sessionKey.CopyTo(sessionKeyCopy, 0);
            _sessionKeyCache.AddOrUpdate(connectId, sessionKeyCopy, (_, existing) =>
            {
                CryptographicOperations.ZeroMemory(existing);
                return sessionKeyCopy;
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
        }
    }

    public async Task<byte[]?> GetSessionKeyAsync(uint connectId)
    {
        if (_sessionKeyCache.TryGetValue(connectId, out byte[]? cachedKey))
        {
            byte[] copy = new byte[cachedKey.Length];
            cachedKey.CopyTo(copy, 0);
            return copy;
        }

        try
        {
            Result<byte[], SecureStorageFailure> result = await storage.LoadStateAsync($"session_{connectId}");
            if (!result.IsOk)
                return null;

            byte[] protectedKey = result.Unwrap();
            byte[] sessionKey = await UnwrapSessionKeyAsync(protectedKey, connectId);

            byte[] sessionKeyCopy = new byte[sessionKey.Length];
            sessionKey.CopyTo(sessionKeyCopy, 0);
            _sessionKeyCache.TryAdd(connectId, sessionKeyCopy);

            return sessionKey;
        }
        catch
        {
            return null;
        }
    }

    public async Task InvalidateSessionKeyAsync(uint connectId)
    {
        try
        {
            if (_sessionKeyCache.TryRemove(connectId, out byte[]? cachedKey))
            {
                CryptographicOperations.ZeroMemory(cachedKey);
            }

            await storage.DeleteStateAsync($"session_{connectId}");

            if (platformProvider.IsHardwareSecurityAvailable())
            {
                string keychainKey = $"ecliptix_session_wrap_{connectId}";
                await platformProvider.DeleteKeyFromKeychainAsync(keychainKey);
            }
        }
        catch
        {
        }
    }

    public async Task InvalidateAllSessionKeysAsync()
    {
        foreach (uint connectId in _sessionKeyCache.Keys)
        {
            await InvalidateSessionKeyAsync(connectId);
        }

        _sessionKeyCache.Clear();
    }

    public async Task<bool> HasValidSessionKeyAsync(uint connectId)
    {
        byte[]? sessionKey = await GetSessionKeyAsync(connectId);
        if (sessionKey != null)
        {
            CryptographicOperations.ZeroMemory(sessionKey);
            return true;
        }

        return false;
    }

    private async Task<byte[]> WrapSessionKeyAsync(byte[] sessionKey, uint connectId)
    {
        if (!platformProvider.IsHardwareSecurityAvailable())
        {
            byte[] copy = new byte[sessionKey.Length];
            sessionKey.CopyTo(copy, 0);
            return copy;
        }

        try
        {
            byte[] wrappingKey = await GenerateWrappingKeyAsync();
            string keychainKey = $"ecliptix_session_wrap_{connectId}";
            await platformProvider.StoreKeyInKeychainAsync(keychainKey, wrappingKey);

            using Aes aes = Aes.Create();
            aes.Key = wrappingKey;
            aes.GenerateIV();

            byte[] encryptedKey = aes.EncryptCbc(sessionKey, aes.IV);

            byte[] wrappedData = new byte[aes.IV.Length + encryptedKey.Length];
            aes.IV.CopyTo(wrappedData, 0);
            encryptedKey.CopyTo(wrappedData, aes.IV.Length);

            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(encryptedKey);

            return wrappedData;
        }
        catch
        {
            byte[] copy = new byte[sessionKey.Length];
            sessionKey.CopyTo(copy, 0);
            return copy;
        }
    }

    private async Task<byte[]> UnwrapSessionKeyAsync(byte[] protectedKey, uint connectId)
    {
        if (!platformProvider.IsHardwareSecurityAvailable())
        {
            byte[] copy = new byte[protectedKey.Length];
            protectedKey.CopyTo(copy, 0);
            return copy;
        }

        try
        {
            string keychainKey = $"ecliptix_session_wrap_{connectId}";
            byte[]? wrappingKey = await platformProvider.GetKeyFromKeychainAsync(keychainKey);

            if (wrappingKey == null)
            {
                byte[] copy = new byte[protectedKey.Length];
                protectedKey.CopyTo(copy, 0);
                return copy;
            }

            using Aes aes = Aes.Create();
            aes.Key = wrappingKey;

            byte[] iv = new byte[aes.BlockSize / 8];
            byte[] encryptedKey = new byte[protectedKey.Length - iv.Length];

            Array.Copy(protectedKey, 0, iv, 0, iv.Length);
            Array.Copy(protectedKey, iv.Length, encryptedKey, 0, encryptedKey.Length);

            aes.IV = iv;
            byte[] sessionKey = aes.DecryptCbc(encryptedKey, iv);

            CryptographicOperations.ZeroMemory(wrappingKey);
            CryptographicOperations.ZeroMemory(encryptedKey);

            return sessionKey;
        }
        catch
        {
            byte[] copy = new byte[protectedKey.Length];
            protectedKey.CopyTo(copy, 0);
            return copy;
        }
    }

    private async Task<byte[]> GenerateWrappingKeyAsync()
    {
        return await platformProvider.GenerateSecureRandomAsync(32);
    }
}