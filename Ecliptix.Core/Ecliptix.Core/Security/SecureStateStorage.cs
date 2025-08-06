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

public sealed class SecureStateStorage : IDisposable
{
    private const int SALT_SIZE = 32;
    private const int NONCE_SIZE = 12;
    private const int TAG_SIZE = 16;
    private const int KEY_SIZE = 32;
    private const int ARGON2_ITERATIONS = 4;
    private const int ARGON2_MEMORY_SIZE = 65536; // 64 MB
    private const int ARGON2_PARALLELISM = 2;
    private const string MAGIC_HEADER = "ECLIPTIX_SECURE_V1";
    private const int CURRENT_VERSION = 1;
    private const int HMAC_SHA512_SIZE = 64;

    private readonly IPlatformSecurityProvider _platformProvider;
    private readonly string _storagePath;
    private readonly byte[] _deviceId;
    private byte[]? _masterKey;
    private bool _disposed;

    public SecureStateStorage(IPlatformSecurityProvider platformProvider, string storagePath, byte[] deviceId)
    {
        _platformProvider = platformProvider ?? throw new ArgumentNullException(nameof(platformProvider));
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        _deviceId = deviceId ?? throw new ArgumentNullException(nameof(deviceId));

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

    public async Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string userId)
    {
        if (_disposed)
            return Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure("Storage is disposed"));

        try
        {
            (byte[] encryptionKey, byte[] salt) = await DeriveKeyAsync(userId);

            byte[] nonce = await _platformProvider.GenerateSecureRandomAsync(NONCE_SIZE);

            byte[] associatedData = CreateAssociatedData(userId, _deviceId);

            (byte[] ciphertext, byte[] tag) = EncryptState(protocolState, encryptionKey, nonce, associatedData);

            byte[] container = CreateSecureContainer(salt, nonce, tag, ciphertext, associatedData);

            byte[] protectedContainer = await AddTamperProtectionAsync(container);

            await WriteSecureFileAsync(protectedContainer);

            await _platformProvider.StoreKeyInKeychainAsync($"ecliptix_key_{userId}", encryptionKey);

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

    public async Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string userId)
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

            byte[] expectedAd = CreateAssociatedData(userId, _deviceId);
            if (!CryptographicOperations.FixedTimeEquals(associatedData, expectedAd))
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("Associated data mismatch"));
            }

            byte[]? storedKey = await _platformProvider.GetKeyFromKeychainAsync($"ecliptix_key_{userId}");
            byte[] encryptionKey;

            if (storedKey != null)
            {
                encryptionKey = storedKey;
            }
            else
            {
                (byte[] derivedKey, byte[] _) = await DeriveKeyWithSaltAsync(userId, salt);
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

    private async Task<(byte[] key, byte[] salt)> DeriveKeyAsync(string userId)
    {
        byte[] salt = await _platformProvider.GenerateSecureRandomAsync(SALT_SIZE);
        (byte[] key, byte[] _) = await DeriveKeyWithSaltAsync(userId, salt);
        return (key, salt);
    }

    private async Task<(byte[] key, byte[] salt)> DeriveKeyWithSaltAsync(string userId, byte[] salt)
    {
        using Argon2id argon2 = new(Encoding.UTF8.GetBytes(userId))
        {
            Salt = salt,
            DegreeOfParallelism = ARGON2_PARALLELISM,
            Iterations = ARGON2_ITERATIONS,
            MemorySize = ARGON2_MEMORY_SIZE
        };

        argon2.AssociatedData = _deviceId;

        byte[] key = await argon2.GetBytesAsync(KEY_SIZE);
        return (key, salt);
    }

    private (byte[] ciphertext, byte[] tag) EncryptState(
        byte[] plaintext, byte[] key, byte[] nonce, byte[] associatedData)
    {
        using AesGcm aesGcm = new AesGcm(key, TAG_SIZE);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TAG_SIZE];

        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, tag);
    }

    private static byte[] DecryptState(
        byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag, byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, TAG_SIZE);
        byte[] plaintext = new byte[ciphertext.Length];

        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    private static byte[] CreateAssociatedData(string userId, byte[] deviceId)
    {
        byte[] userIdBytes = Encoding.UTF8.GetBytes(userId);
        byte[] ad = new byte[userIdBytes.Length + deviceId.Length + 4];

        BitConverter.GetBytes(CURRENT_VERSION).CopyTo(ad, 0);
        userIdBytes.CopyTo(ad, 4);
        deviceId.CopyTo(ad, 4 + userIdBytes.Length);

        return ad;
    }

    private static byte[] CreateSecureContainer(
        byte[] salt, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] associatedData)
    {
        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        writer.Write(Encoding.ASCII.GetBytes(MAGIC_HEADER));
        writer.Write(CURRENT_VERSION);

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

        string magic = Encoding.ASCII.GetString(reader.ReadBytes(MAGIC_HEADER.Length));
        if (magic != MAGIC_HEADER)
            throw new InvalidOperationException("Invalid container format");

        int version = reader.ReadInt32();
        if (version != CURRENT_VERSION)
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
        if (protectedData.Length < HMAC_SHA512_SIZE)
            return null;

        byte[] data = protectedData[..^HMAC_SHA512_SIZE];
        byte[] mac = protectedData[^HMAC_SHA512_SIZE..];

        byte[] hmacKey = await _platformProvider.GetOrCreateHmacKeyAsync();
        using HMACSHA512 hmac = new HMACSHA512(hmacKey);
        byte[] expectedMac = hmac.ComputeHash(data);

        if (!CryptographicOperations.FixedTimeEquals(mac, expectedMac))
            return null;

        return data;
    }

    private async Task WriteSecureFileAsync(byte[] data)
    {
        string tempPath = $"{_storagePath}.tmp";

        await File.WriteAllBytesAsync(tempPath, data);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(tempPath, _storagePath, true);
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

public record SecureStorageFailure : FailureBase
{
    public SecureStorageFailure(string message) : base(message)
    {
    }

    public override object ToStructuredLog() => new { Message, Type = "SecureStorageFailure" };
}