using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Constants;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Konscious.Security.Cryptography;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.Storage;

public sealed class SecureProtocolStateStorage : ISecureProtocolStateStorage, IDisposable
{

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
            return Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.StorageDisposed));

        try
        {
            (byte[] encryptionKey, byte[] salt) = await DeriveKeyAsync(membershipId).ConfigureAwait(false);

            byte[] nonce = await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Encryption.NonceSize).ConfigureAwait(false);

            byte[] associatedData = CreateAssociatedData(connectId, _deviceId);

            (byte[] ciphertext, byte[] tag) = EncryptState(protocolState, encryptionKey, nonce, associatedData);

            byte[] container = CreateSecureContainer(salt, nonce, tag, ciphertext, associatedData);

            byte[] protectedContainer = await AddTamperProtectionAsync(container).ConfigureAwait(false);

            string storagePath = GetStorageFilePath(connectId);
            await WriteSecureFileAsync(protectedContainer, storagePath).ConfigureAwait(false);

            await _platformProvider.StoreKeyInKeychainAsync($"ecliptix_key_{connectId}", encryptionKey).ConfigureAwait(false);

            CryptographicOperations.ZeroMemory(encryptionKey);
            CryptographicOperations.ZeroMemory(protocolState);

            Log.Information("[CLIENT-STATE-SAVE] Protocol state saved. ConnectId: {ConnectId}, FilePath: {FilePath}",
                connectId, storagePath);

            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure(string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.SaveFailed, ex.Message)));
        }
    }

    public async Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId, byte[] membershipId)
    {
        if (_disposed)
            return Result<byte[], SecureStorageFailure>.Err(new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.StorageDisposed));

        try
        {
            string storagePath = GetStorageFilePath(connectId);
            byte[]? protectedContainer = await ReadSecureFileAsync(storagePath).ConfigureAwait(false);
            if (protectedContainer == null)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.StateFileNotFound));
            }

            byte[]? container = await VerifyTamperProtectionAsync(protectedContainer).ConfigureAwait(false);
            if (container == null)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.TamperedStateDetected));
            }

            (byte[] salt, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] associatedData) =
                ParseSecureContainer(container);

            byte[] expectedAd = CreateAssociatedData(connectId, _deviceId);
            if (!CryptographicOperations.FixedTimeEquals(associatedData, expectedAd))
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.AssociatedDataMismatch));
            }

            byte[]? storedKey = await _platformProvider.GetKeyFromKeychainAsync($"ecliptix_key_{connectId}").ConfigureAwait(false);
            byte[] encryptionKey;

            if (storedKey != null)
            {
                encryptionKey = storedKey;
            }
            else
            {
                (byte[] derivedKey, byte[] _) = await DeriveKeyWithSaltAsync(membershipId, salt).ConfigureAwait(false);
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

                (byte[] legacyKey, byte[] _) = await DeriveKeyWithSaltAsync(Encoding.UTF8.GetBytes(connectId), salt).ConfigureAwait(false);

                try
                {
                    byte[] plaintext = DecryptState(ciphertext, legacyKey, nonce, tag, associatedData);

                    Log.Warning("[CLIENT-STATE-LOAD-MIGRATION] Legacy decryption succeeded. Re-saving with membershipId. ConnectId: {ConnectId}",
                        connectId);

                    await SaveStateAsync(plaintext, connectId, membershipId).ConfigureAwait(false);

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
                new SecureStorageFailure(string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.LoadFailed, ex.Message)));
        }
    }

    public async Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key)
    {
        if (_disposed)
            return Result<Unit, SecureStorageFailure>.Err(new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.StorageDisposed));

        try
        {
            string storagePath = GetStorageFilePath(key);
            await DeleteFileWithRetryAsync(storagePath).ConfigureAwait(false);
            await _platformProvider.DeleteKeyFromKeychainAsync($"ecliptix_key_{key}").ConfigureAwait(false);

            Log.Information("[CLIENT-STATE-DELETE] Protocol state deleted securely. Key: {Key}, FilePath: {FilePath}",
                key, storagePath);
            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[CLIENT-STATE-DELETE-ERROR] Failed to delete secure state. Key: {Key}, FilePath: {FilePath}",
                key, GetStorageFilePath(key));
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure(string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.DeleteFailed, ex.Message)));
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
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
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
        byte[] salt = await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Encryption.SaltSize).ConfigureAwait(false);
        (byte[] key, byte[] _) = await DeriveKeyWithSaltAsync(membershipId, salt).ConfigureAwait(false);
        return (key, salt);
    }

    private async Task<(byte[] key, byte[] salt)> DeriveKeyWithSaltAsync(byte[] membershipId, byte[] salt)
    {
        using Argon2id argon2 = new(membershipId)
        {
            Salt = salt,
            DegreeOfParallelism = SecureStorageConstants.Argon2.Parallelism,
            Iterations = SecureStorageConstants.Argon2.Iterations,
            MemorySize = SecureStorageConstants.Argon2.MemorySize
        };

        argon2.AssociatedData = _deviceId;

        byte[] key = await argon2.GetBytesAsync(SecureStorageConstants.Encryption.KeySize).ConfigureAwait(false);
        return (key, salt);
    }

    private static (byte[] ciphertext, byte[] tag) EncryptState(
        byte[] plaintext, byte[] key, byte[] nonce, byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, SecureStorageConstants.Encryption.TagSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[SecureStorageConstants.Encryption.TagSize];

        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, tag);
    }

    private static byte[] DecryptState(
        byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag, byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, SecureStorageConstants.Encryption.TagSize);
        byte[] plaintext = new byte[ciphertext.Length];

        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    private static byte[] CreateAssociatedData(string connectId, byte[] deviceId)
    {
        byte[] connectIdBytes = Encoding.UTF8.GetBytes(connectId);
        byte[] ad = new byte[connectIdBytes.Length + deviceId.Length + 4];

        BitConverter.GetBytes(SecureStorageConstants.Header.CurrentVersion).CopyTo(ad, 0);
        connectIdBytes.CopyTo(ad, 4);
        deviceId.CopyTo(ad, 4 + connectIdBytes.Length);

        return ad;
    }

    private static byte[] CreateSecureContainer(
        byte[] salt, byte[] nonce, byte[] tag, byte[] ciphertext, byte[] associatedData)
    {
        byte[] magicBytes = Encoding.ASCII.GetBytes(SecureStorageConstants.Header.MagicHeader);

        int totalSize = magicBytes.Length + 4 +
                       4 + salt.Length +
                       4 + nonce.Length +
                       4 + tag.Length +
                       4 + associatedData.Length +
                       4 + ciphertext.Length;

        byte[] container = new byte[totalSize];
        int offset = 0;

        magicBytes.CopyTo(container, offset);
        offset += magicBytes.Length;

        BitConverter.GetBytes(SecureStorageConstants.Header.CurrentVersion).CopyTo(container, offset);
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

        string magic = Encoding.ASCII.GetString(reader.ReadBytes(SecureStorageConstants.Header.MagicHeader.Length));
        if (magic != SecureStorageConstants.Header.MagicHeader)
            throw new InvalidOperationException(ApplicationErrorMessages.SecureProtocolStateStorage.InvalidContainerFormat);

        int version = reader.ReadInt32();
        if (version != SecureStorageConstants.Header.CurrentVersion)
            throw new InvalidOperationException(string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.UnsupportedVersion, version));

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
        byte[] hmacKey = await _platformProvider.GetOrCreateHmacKeyAsync().ConfigureAwait(false);
        using HMACSHA512 hmac = new(hmacKey);
        byte[] mac = hmac.ComputeHash(data);

        byte[] result = new byte[data.Length + mac.Length];
        data.CopyTo(result, 0);
        mac.CopyTo(result, data.Length);

        return result;
    }

    private async Task<byte[]?> VerifyTamperProtectionAsync(byte[] protectedData)
    {
        if (protectedData.Length < SecureStorageConstants.Encryption.HmacSha512Size)
            return null;

        byte[] data = protectedData[..^SecureStorageConstants.Encryption.HmacSha512Size];
        byte[] mac = protectedData[^SecureStorageConstants.Encryption.HmacSha512Size..];

        byte[] hmacKey = await _platformProvider.GetOrCreateHmacKeyAsync().ConfigureAwait(false);
        using HMACSHA512 hmac = new(hmacKey);
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
            await File.WriteAllBytesAsync(tempPath, data).ConfigureAwait(false);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            await DeleteFileWithRetryAsync(filePath).ConfigureAwait(false);

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
        !File.Exists(filePath) ? null : await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);

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
