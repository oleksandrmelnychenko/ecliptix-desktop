using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Konscious.Security.Cryptography;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.Storage;

public sealed class SecureProtocolStateStorage : ISecureProtocolStateStorage, IDisposable
{
    private const int SaltSize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;
    private const int Argon2Iterations = 4;
    private const int Argon2MemorySize = 65536;
    private const int Argon2Parallelism = 2;
    private const string MagicHeader = "ECLIPTIX_SECURE_V1";
    private const int CurrentVersion = 1;
    private const int HmacSha512Size = 64;

    private readonly IPlatformSecurityProvider _platformProvider;
    private readonly string _storageDirectory;
    private readonly byte[] _deviceId;
    private byte[]? _masterKey;
    private bool _disposed;

    public SecureProtocolStateStorage(IPlatformSecurityProvider platformProvider, string storageDirectory, byte[] deviceId)
    {
        _platformProvider = platformProvider;
        _storageDirectory = storageDirectory;
        _deviceId = deviceId;

        InitializeSecureStorage();
    }

    private void InitializeSecureStorage()
    {
        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(_storageDirectory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    private string GetStorageFilePath(string connectId)
    {
        string sanitizedKey = SanitizeFilename(connectId);
        return Path.Combine(_storageDirectory, $"{sanitizedKey}.ecliptix");
    }

    private static string SanitizeFilename(string key)
    {
        string invalid = new(Path.GetInvalidFileNameChars());
        foreach (char c in invalid)
        {
            key = key.Replace(c, '_');
        }
        return key;
    }

    public async Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string connectId, byte[] membershipId)
    {
        if (_disposed)
            return Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure("Storage is disposed"));

        try
        {
            (byte[] encryptionKey, byte[] salt) = await DeriveKeyAsync(membershipId);

            byte[] nonce = await _platformProvider.GenerateSecureRandomAsync(NonceSize);

            byte[] associatedData = CreateAssociatedData(connectId, _deviceId);

            (byte[] ciphertext, byte[] tag) = EncryptState(protocolState, encryptionKey, nonce, associatedData);

            byte[] container = CreateSecureContainer(salt, nonce, tag, ciphertext, associatedData);

            byte[] protectedContainer = await AddTamperProtectionAsync(container);

            string storagePath = GetStorageFilePath(connectId);
            await WriteSecureFileAsync(protectedContainer, storagePath);

            await _platformProvider.StoreKeyInKeychainAsync($"ecliptix_key_{connectId}", encryptionKey);

            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(protocolState);

            Log.Information("[CLIENT-STATE-SAVE] Protocol state saved. ConnectId: {ConnectId}, FilePath: {FilePath}",
                connectId, storagePath);

            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Save failed: {ex.Message}"));
        }
    }

    public Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(ReadOnlySpan<byte> protocolState, string connectId, byte[] membershipId)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(protocolState.Length);
        int length = protocolState.Length;
        try
        {
            protocolState.CopyTo(buffer.AsSpan(0, length));
            Task<Result<Unit, SecureStorageFailure>> result = SaveStateAsync(buffer.AsMemory(0, length).ToArray(), connectId, membershipId);
            return result.ContinueWith(t =>
            {
                CryptographicOperations.ZeroMemory(buffer.AsSpan(0, length));
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
                return t.Result;
            });
        }
        catch
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, length));
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            throw;
        }
    }

    public async Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId, byte[] membershipId)
    {
        if (_disposed)
            return Result<byte[], SecureStorageFailure>.Err(new SecureStorageFailure("Storage is disposed"));

        try
        {
            string storagePath = GetStorageFilePath(connectId);
            byte[]? protectedContainer = await ReadSecureFileAsync(storagePath);
            if (protectedContainer == null)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure("State file not found"));
            }

            byte[]? container = await VerifyTamperProtectionAsync(protectedContainer);
            if (container == null)
            {
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
                (byte[] derivedKey, byte[] _) = await DeriveKeyWithSaltAsync(membershipId, salt);
                encryptionKey = derivedKey;
            }

            try
            {
                byte[] plaintext = DecryptState(ciphertext, encryptionKey, nonce, tag, associatedData);

                Log.Information("[CLIENT-STATE-LOAD] Protocol state loaded successfully with membershipId. ConnectId: {ConnectId}",
                    connectId);

                return Result<byte[], SecureStorageFailure>.Ok(plaintext);
            }
            catch (CryptographicException) when (storedKey == null)
            {
                Log.Warning("[CLIENT-STATE-LOAD-MIGRATION] Attempting legacy decryption with connectId. ConnectId: {ConnectId}",
                    connectId);

                CryptographicOperations.ZeroMemory(encryptionKey);

                (byte[] legacyKey, byte[] _) = await DeriveKeyWithSaltAsync(Encoding.UTF8.GetBytes(connectId), salt);

                try
                {
                    byte[] plaintext = DecryptState(ciphertext, legacyKey, nonce, tag, associatedData);

                    Log.Warning("[CLIENT-STATE-LOAD-MIGRATION] Legacy decryption succeeded. Re-saving with membershipId. ConnectId: {ConnectId}",
                        connectId);

                    await SaveStateAsync(plaintext, connectId, membershipId);

                    CryptographicOperations.ZeroMemory(legacyKey);
                    return Result<byte[], SecureStorageFailure>.Ok(plaintext);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(legacyKey);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(encryptionKey);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-STATE-LOAD-ERROR] Failed to load secure state. ConnectId: {ConnectId}, FilePath: {FilePath}",
                connectId, GetStorageFilePath(connectId));
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
            string storagePath = GetStorageFilePath(key);
            await DeleteFileWithRetryAsync(storagePath);
            await _platformProvider.DeleteKeyFromKeychainAsync($"ecliptix_key_{key}");

            Log.Information("[CLIENT-STATE-DELETE] Protocol state deleted securely. Key: {Key}, FilePath: {FilePath}",
                key, storagePath);
            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-STATE-DELETE-ERROR] Failed to delete secure state. Key: {Key}, FilePath: {FilePath}",
                key, GetStorageFilePath(key));
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure($"Delete failed: {ex.Message}"));
        }
    }

    private static async Task DeleteFileWithRetryAsync(string filePath, int maxRetries = 3)
    {
        if (!File.Exists(filePath))
            return;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                if (File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(filePath, File.GetAttributes(filePath) & ~FileAttributes.ReadOnly);
                }

                File.Delete(filePath);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                await Task.Delay(100 * (attempt + 1));
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(100 * (attempt + 1));
            }
            catch (FileNotFoundException)
            {
                return;
            }
        }
        File.Delete(filePath);
    }

    private async Task<(byte[] key, byte[] salt)> DeriveKeyAsync(byte[] membershipId)
    {
        byte[] salt = await _platformProvider.GenerateSecureRandomAsync(SaltSize);
        (byte[] key, byte[] _) = await DeriveKeyWithSaltAsync(membershipId, salt);
        return (key, salt);
    }

    private async Task<(byte[] key, byte[] salt)> DeriveKeyWithSaltAsync(byte[] membershipId, byte[] salt)
    {
        using Argon2id argon2 = new(membershipId)
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

    private static (byte[] ciphertext, byte[] tag) EncryptStateFromSpan(
        ReadOnlySpan<byte> plaintext, byte[] key, byte[] nonce, byte[] associatedData)
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
        byte[] magicBytes = Encoding.ASCII.GetBytes(MagicHeader);

        int totalSize = magicBytes.Length + 4 + // magic header + version
                       4 + salt.Length +        // salt length + salt
                       4 + nonce.Length +       // nonce length + nonce
                       4 + tag.Length +         // tag length + tag
                       4 + associatedData.Length + // ad length + ad
                       4 + ciphertext.Length;   // ciphertext length + ciphertext

        byte[] container = new byte[totalSize];
        int offset = 0;

        magicBytes.CopyTo(container, offset);
        offset += magicBytes.Length;

        BitConverter.GetBytes(CurrentVersion).CopyTo(container, offset);
        offset += 4;

        BitConverter.GetBytes(salt.Length).CopyTo(container, offset);
        offset += 4;
        salt.CopyTo(container, offset);
        offset += salt.Length;

        BitConverter.GetBytes(nonce.Length).CopyTo(container, offset);
        offset += 4;
        nonce.CopyTo(container, offset);
        offset += nonce.Length;

        BitConverter.GetBytes(tag.Length).CopyTo(container, offset);
        offset += 4;
        tag.CopyTo(container, offset);
        offset += tag.Length;

        BitConverter.GetBytes(associatedData.Length).CopyTo(container, offset);
        offset += 4;
        associatedData.CopyTo(container, offset);
        offset += associatedData.Length;

        BitConverter.GetBytes(ciphertext.Length).CopyTo(container, offset);
        offset += 4;
        ciphertext.CopyTo(container, offset);

        return container;
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

    private async Task WriteSecureFileAsync(byte[] data, string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(directory,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        string tempPath = $"{filePath}.tmp.{Guid.NewGuid():N}";

        try
        {
            await File.WriteAllBytesAsync(tempPath, data);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await DeleteFileWithRetryAsync(filePath);

            File.Move(tempPath, filePath);
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

    private async Task<byte[]?> ReadSecureFileAsync(string filePath) =>
        !File.Exists(filePath) ? null : await File.ReadAllBytesAsync(filePath);

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