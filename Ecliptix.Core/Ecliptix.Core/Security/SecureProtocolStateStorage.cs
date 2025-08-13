using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Konscious.Security.Cryptography;
using Serilog;

namespace Ecliptix.Core.Security;

public sealed class SecureProtocolStateStorage : ISecureProtocolStateStorage, IDisposable
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Argon2Iterations = 4;
    private const int Argon2MemorySize = 65536; // 64 MB
    private const int Argon2Parallelism = 2;
    private const string MagicHeader = "ECLIPTIX_SECURE_V1";
    private const int CurrentVersion = 1;
    private const int HmacSha512Size = 64;

    private readonly IPlatformSecurityProvider _platformProvider;
    private readonly string _storagePath;
    private readonly byte[] _deviceId;
    private byte[]? _masterKey;
    private bool _disposed;

    public SecureProtocolStateStorage(IPlatformSecurityProvider platformProvider, string storagePath, byte[] deviceId)
    {
        _platformProvider = platformProvider;
        _storagePath = storagePath;
        _deviceId = deviceId;

        InitializeSecureStorage();
    }

    private void InitializeSecureStorage()
    {
        string? directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    public async Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string connectId)
    {
        if (_disposed)
            return Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure("Storage is disposed"));

        try
        {
            (byte[] encryptionKey, byte[] salt) = await DeriveKeyAsync(connectId);

            byte[] nonce = await _platformProvider.GenerateSecureRandomAsync(NonceSize);

            byte[] associatedData = CreateAssociatedData(connectId, _deviceId);

            (byte[] ciphertext, byte[] tag) = EncryptState(protocolState, encryptionKey, nonce, associatedData);

            byte[] container = CreateSecureContainer(salt, nonce, tag, ciphertext, associatedData);

            byte[] protectedContainer = await AddTamperProtectionAsync(container);

            await WriteSecureFileAsync(protectedContainer);

            await _platformProvider.StoreKeyInKeychainAsync($"ecliptix_key_{connectId}", encryptionKey);

            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(protocolState);

            Log.Information("Protocol state saved securely with hardware protection");
            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save secure state");
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Save failed: {ex.Message}"));
        }
    }

    public async Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId)
    {
        if (_disposed)
            return Result<byte[], SecureStorageFailure>.Err(new SecureStorageFailure("Storage is disposed"));

        try
        {
            byte[]? protectedContainer = await ReadSecureFileAsync();
            if (protectedContainer == null)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("State file not found"));
            }

            byte[]? container = await VerifyTamperProtectionAsync(protectedContainer);
            if (container == null)
            {
                Log.Error("SECURITY ALERT: State file has been tampered with!");
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Security violation: tampered state detected"));
            }

            (byte[] salt, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] associatedData) =
                ParseSecureContainer(container);

            byte[] expectedAd = CreateAssociatedData(connectId, _deviceId);
            if (!CryptographicOperations.FixedTimeEquals(associatedData, expectedAd))
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Associated data mismatch"));
            }

            byte[]? storedKey = await _platformProvider.GetKeyFromKeychainAsync($"ecliptix_key_{connectId}");
            byte[] encryptionKey;

            if (storedKey != null)
            {
                encryptionKey = storedKey;
            }
            else
            {
                (byte[] derivedKey, byte[] _) = await DeriveKeyWithSaltAsync(connectId, salt);
                encryptionKey = derivedKey;
            }

            byte[] plaintext = DecryptState(ciphertext, encryptionKey, nonce, tag, associatedData);

            CryptographicOperations.ZeroMemory(encryptionKey);

            Log.Information("Protocol state loaded and verified successfully");
            return Result<byte[], SecureStorageFailure>.Ok(plaintext);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load secure state");
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure($"Load failed: {ex.Message}"));
        }
    }

    public async Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key)
    {
        if (_disposed)
            return Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure("Storage is disposed"));

        try
        {
            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
            }

            await _platformProvider.DeleteKeyFromKeychainAsync($"ecliptix_key_{key}");

            Log.Information("Protocol state deleted securely for user {Key}", key);
            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete secure state for user {Key}", key);
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Delete failed: {ex.Message}"));
        }
    }

    private async Task<(byte[] key, byte[] salt)> DeriveKeyAsync(string connectId)
    {
        byte[] salt = await _platformProvider.GenerateSecureRandomAsync(SaltSize);
        (byte[] key, byte[] _) = await DeriveKeyWithSaltAsync(connectId, salt);
        return (key, salt);
    }

    private async Task<(byte[] key, byte[] salt)> DeriveKeyWithSaltAsync(string connectId, byte[] salt)
    {
        using Argon2id argon2 = new(Encoding.UTF8.GetBytes(connectId))
        {
            Salt = salt,
            DegreeOfParallelism = Argon2Parallelism,
            Iterations = Argon2Iterations,
            MemorySize = Argon2MemorySize
        };

        argon2.AssociatedData = _deviceId;

        byte[] key = await argon2.GetBytesAsync(KeySize);
        return (key, salt);
    }

    private static (byte[] ciphertext, byte[] tag) EncryptState(
        byte[] plaintext, byte[] key, byte[] nonce, byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, TagSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, tag);
    }

    private static byte[] DecryptState(
        byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag, byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, TagSize);
        byte[] plaintext = new byte[ciphertext.Length];

        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    private static byte[] CreateAssociatedData(string connectId, byte[] deviceId)
    {
        byte[] connectIdBytes = Encoding.UTF8.GetBytes(connectId);
        byte[] ad = new byte[connectIdBytes.Length + deviceId.Length + 4];

        BitConverter.GetBytes(CurrentVersion).CopyTo(ad, 0);
        connectIdBytes.CopyTo(ad, 4);
        deviceId.CopyTo(ad, 4 + connectIdBytes.Length);

        return ad;
    }

    private static byte[] CreateSecureContainer(
        byte[] salt, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] associatedData)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(Encoding.ASCII.GetBytes(MagicHeader));
        writer.Write(CurrentVersion);

        writer.Write(salt.Length);
        writer.Write(salt);

        writer.Write(nonce.Length);
        writer.Write(nonce);

        writer.Write(tag.Length);
        writer.Write(tag);

        writer.Write(associatedData.Length);
        writer.Write(associatedData);

        writer.Write(ciphertext.Length);
        writer.Write(ciphertext);

        return ms.ToArray();
    }

    private static (byte[] salt, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] associatedData)
        ParseSecureContainer(byte[] container)
    {
        using MemoryStream ms = new(container);
        using BinaryReader reader = new(ms);

        string magic = Encoding.ASCII.GetString(reader.ReadBytes(MagicHeader.Length));
        if (magic != MagicHeader)
            throw new InvalidOperationException("Invalid container format");

        int version = reader.ReadInt32();
        if (version != CurrentVersion)
            throw new InvalidOperationException($"Unsupported version: {version}");

        int saltLength = reader.ReadInt32();
        byte[] salt = reader.ReadBytes(saltLength);

        int nonceLength = reader.ReadInt32();
        byte[] nonce = reader.ReadBytes(nonceLength);

        int tagLength = reader.ReadInt32();
        byte[] tag = reader.ReadBytes(tagLength);

        int adLength = reader.ReadInt32();
        byte[] associatedData = reader.ReadBytes(adLength);

        int ciphertextLength = reader.ReadInt32();
        byte[] ciphertext = reader.ReadBytes(ciphertextLength);

        return (salt, nonce, tag, ciphertext, associatedData);
    }

    private async Task<byte[]> AddTamperProtectionAsync(byte[] data)
    {
        byte[] hmacKey = await _platformProvider.GetOrCreateHmacKeyAsync();
        using HMACSHA512 hmac = new(hmacKey);
        byte[] mac = hmac.ComputeHash(data);

        byte[] result = new byte[data.Length + mac.Length];
        data.CopyTo(result, 0);
        mac.CopyTo(result, data.Length);

        return result;
    }

    private async Task<byte[]?> VerifyTamperProtectionAsync(byte[] protectedData)
    {
        if (protectedData.Length < HmacSha512Size)
            return null;

        byte[] data = protectedData[..^HmacSha512Size];
        byte[] mac = protectedData[^HmacSha512Size..];

        byte[] hmacKey = await _platformProvider.GetOrCreateHmacKeyAsync();
        using HMACSHA512 hmac = new HMACSHA512(hmacKey);
        byte[] expectedMac = hmac.ComputeHash(data);

        if (!CryptographicOperations.FixedTimeEquals(mac, expectedMac))
            return null;

        return data;
    }

    private async Task WriteSecureFileAsync(byte[] data)
    {
        string? directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        string tempPath = $"{_storagePath}.tmp";

        try
        {
            await File.WriteAllBytesAsync(tempPath, data);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            if (File.Exists(_storagePath))
            {
                File.Delete(_storagePath);
            }

            File.Move(tempPath, _storagePath);
        }
        catch (Exception)
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
            throw;
        }
    }

    private async Task<byte[]?> ReadSecureFileAsync() =>
        !File.Exists(_storagePath) ? null : await File.ReadAllBytesAsync(_storagePath);

    public void Dispose()
    {
        if (_disposed) return;

        if (_masterKey != null)
        {
            CryptographicOperations.ZeroMemory(_masterKey);
            _masterKey = null;
        }

        _disposed = true;
    }
}

public record SecureStorageFailure(string Message) : FailureBase(Message)
{
    public override object ToStructuredLog() => new { Message, Type = "SecureStorageFailure" };
}