using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Constants;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Grpc.Core;
using Konscious.Security.Cryptography;

namespace Ecliptix.Core.Infrastructure.Security.Storage;

public sealed class SecureProtocolStateStorage : ISecureProtocolStateStorage, IDisposable
{
    private const string KeychainPrefix = "ecliptix_key_";

    private static readonly Encoding Utf8 = Encoding.UTF8;
    private static readonly Encoding Ascii = Encoding.ASCII;
    private static readonly byte[] MagicHeaderBytes = Ascii.GetBytes(SecureStorageConstants.Header.MAGIC_HEADER);
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();
    private static readonly HashSet<char> InvalidFileNameCharacterSet = new(InvalidFileNameChars);

    private readonly IPlatformSecurityProvider _platformProvider;
    private readonly string _storageDirectory;
    private readonly byte[] _deviceId;

    private byte[]? _cachedHmacKey;
    private bool _disposed;

    public SecureProtocolStateStorage(IPlatformSecurityProvider platformProvider, string storageDirectory,
        byte[] deviceId)
    {
        _platformProvider = platformProvider;
        _storageDirectory = storageDirectory;
        _deviceId = deviceId;

        EnsureSecureDirectoryExists();
    }

    public async Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(
        byte[] protocolState,
        string connectId,
        byte[] membershipId)
    {
        if (!TryEnsureNotDisposed(out SecureStorageFailure? failure))
        {
            return Result<Unit, SecureStorageFailure>.Err(failure!);
        }

        string storagePath = GetStorageFilePath(connectId);
        string keychainKey = BuildKeychainKey(connectId);

        byte[]? encryptionKey = null;
        byte[]? salt = null;
        byte[]? nonce = null;
        byte[]? tag = null;
        byte[]? ciphertext = null;
        byte[]? containerBytes = null;
        byte[]? protectedContainer = null;
        byte[]? associatedData = null;

        try
        {
            (encryptionKey, salt) = await DeriveKeyAsync(membershipId).ConfigureAwait(false);
            nonce = await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Encryption.NONCE_SIZE)
                .ConfigureAwait(false);

            associatedData = CreateAssociatedData(connectId, _deviceId);

            (ciphertext, tag) = EncryptState(protocolState, encryptionKey, nonce, associatedData);

            containerBytes = SerializeContainer(new SecureContainer(salt, nonce, tag, ciphertext, associatedData));
            protectedContainer = await AddTamperProtectionAsync(containerBytes).ConfigureAwait(false);

            await WriteSecureFileAsync(protectedContainer, storagePath).ConfigureAwait(false);

            await _platformProvider.StoreKeyInKeychainAsync(keychainKey, encryptionKey).ConfigureAwait(false);

            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await CleanupFailedSaveAsync(storagePath, keychainKey).ConfigureAwait(false);

            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure(
                    string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.SAVE_FAILED, ex.Message)));
        }
        finally
        {
            ZeroBuffer(encryptionKey);
            ZeroBuffer(salt);
            ZeroBuffer(nonce);
            ZeroBuffer(tag);
            ZeroBuffer(ciphertext);
            ZeroBuffer(containerBytes);
            ZeroBuffer(protectedContainer);
            ZeroBuffer(associatedData);
            ZeroBuffer(protocolState);
        }
    }

    public async Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId, byte[] membershipId)
    {
        if (!TryEnsureNotDisposed(out SecureStorageFailure? failure))
        {
            return Result<byte[], SecureStorageFailure>.Err(failure!);
        }

        string storagePath = GetStorageFilePath(connectId);
        string keychainKey = BuildKeychainKey(connectId);

        byte[]? protectedContainer = null;
        byte[]? containerBytes = null;
        SecureContainer container = default;
        bool containerInitialized = false;
        byte[]? encryptionKey = null;
        byte[]? expectedAssociatedData = null;

        try
        {
            Option<byte[]> protectedContainerOption = await ReadSecureFileAsync(storagePath).ConfigureAwait(false);
            if (!protectedContainerOption.IsSome)
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.STATE_FILE_NOT_FOUND));
            }

            protectedContainer = protectedContainerOption.Value!;

            Result<byte[], SecureStorageFailure> verifiedResult =
                await VerifyTamperProtectionAsync(protectedContainer).ConfigureAwait(false);

            if (verifiedResult.IsErr)
            {
                return Result<byte[], SecureStorageFailure>.Err(verifiedResult.UnwrapErr());
            }

            containerBytes = verifiedResult.Unwrap();
            container = ParseSecureContainer(containerBytes);
            containerInitialized = true;

            expectedAssociatedData = CreateAssociatedData(connectId, _deviceId);
            if (!CryptographicOperations.FixedTimeEquals(container.AssociatedData, expectedAssociatedData))
            {
                return Result<byte[], SecureStorageFailure>.Err(
                    new SecureStorageFailure(
                        ApplicationErrorMessages.SecureProtocolStateStorage.ASSOCIATED_DATA_MISMATCH));
            }

            Option<byte[]> storedKeyOption = await TryGetStoredKeyAsync(keychainKey).ConfigureAwait(false);
            bool derivedKey = !storedKeyOption.IsSome;
            if (storedKeyOption.IsSome)
            {
                encryptionKey = storedKeyOption.Value!;
            }
            else
            {
                (encryptionKey, _) = await DeriveKeyWithSaltAsync(membershipId, container.Salt).ConfigureAwait(false);
            }

            byte[] plaintext =
                DecryptState(container.Ciphertext, encryptionKey, container.Nonce, container.Tag,
                    container.AssociatedData);

            if (derivedKey)
            {
                await _platformProvider.StoreKeyInKeychainAsync(keychainKey, encryptionKey!).ConfigureAwait(false);
            }

            return Result<byte[], SecureStorageFailure>.Ok(plaintext);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure(
                    string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.LOAD_FAILED, ex.Message)));
        }
        finally
        {
            ZeroBuffer(protectedContainer);
            ZeroBuffer(containerBytes);
            ZeroBuffer(expectedAssociatedData);

            if (containerInitialized)
            {
                ZeroBuffer(container.Salt);
                ZeroBuffer(container.Nonce);
                ZeroBuffer(container.Tag);
                ZeroBuffer(container.Ciphertext);
                ZeroBuffer(container.AssociatedData);
            }

            ZeroBuffer(encryptionKey);
        }
    }

    public async Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key)
    {
        if (!TryEnsureNotDisposed(out SecureStorageFailure? failure))
        {
            return Result<Unit, SecureStorageFailure>.Err(failure!);
        }

        string storagePath = GetStorageFilePath(key);
        string keychainKey = BuildKeychainKey(key);

        try
        {
            await DeleteFileIfExistsAsync(storagePath).ConfigureAwait(false);
            await _platformProvider.DeleteKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);

            return Result<Unit, SecureStorageFailure>.Ok(Unit.Value);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result<Unit, SecureStorageFailure>.Err(
                new SecureStorageFailure(
                    string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.DELETE_FAILED, ex.Message)));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        byte[]? cachedHmacKey = Interlocked.Exchange(ref _cachedHmacKey, null);
        if (cachedHmacKey != null)
        {
            ZeroBuffer(cachedHmacKey);
        }

        _disposed = true;
    }

    private static string BuildKeychainKey(string connectId) => $"{KeychainPrefix}{connectId}";

    private string GetStorageFilePath(string connectId)
    {
        string sanitizedKey = SanitizeFilename(connectId);
        return Path.Combine(_storageDirectory, $"{sanitizedKey}.ecliptix");
    }

    private static string SanitizeFilename(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "_";
        }

        if (key.IndexOfAny(InvalidFileNameChars) < 0)
        {
            return key;
        }

        char[] characters = key.ToCharArray();
        for (int i = 0; i < characters.Length; i++)
        {
            if (InvalidFileNameCharacterSet.Contains(characters[i]))
            {
                characters[i] = '_';
            }
        }

        return new string(characters);
    }

    private static byte[] CreateAssociatedData(string connectId, byte[] deviceId)
    {
        byte[] connectIdBytes = Utf8.GetBytes(connectId);
        byte[] associatedData = new byte[sizeof(int) + connectIdBytes.Length + deviceId.Length];

        BinaryPrimitives.WriteInt32LittleEndian(associatedData.AsSpan(0, sizeof(int)),
            SecureStorageConstants.Header.CURRENT_VERSION);
        connectIdBytes.CopyTo(associatedData.AsSpan(sizeof(int)));
        deviceId.CopyTo(associatedData.AsSpan(sizeof(int) + connectIdBytes.Length));

        return associatedData;
    }

    private static (byte[] Ciphertext, byte[] Tag) EncryptState(
        byte[] plaintext,
        byte[] key,
        byte[] nonce,
        byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, SecureStorageConstants.Encryption.TAG_SIZE);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[SecureStorageConstants.Encryption.TAG_SIZE];

        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return (ciphertext, tag);
    }

    private static byte[] DecryptState(
        byte[] ciphertext,
        byte[] key,
        byte[] nonce,
        byte[] tag,
        byte[] associatedData)
    {
        using AesGcm aesGcm = new(key, SecureStorageConstants.Encryption.TAG_SIZE);
        byte[] plaintext = new byte[ciphertext.Length];

        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    private static byte[] SerializeContainer(SecureContainer container)
    {
        int totalSize = MagicHeaderBytes.Length + sizeof(int) +
                        SegmentSize(container.Salt.Length) +
                        SegmentSize(container.Nonce.Length) +
                        SegmentSize(container.Tag.Length) +
                        SegmentSize(container.AssociatedData.Length) +
                        SegmentSize(container.Ciphertext.Length);

        byte[] buffer = new byte[totalSize];
        Span<byte> writeSpan = buffer.AsSpan();

        MagicHeaderBytes.CopyTo(writeSpan);
        writeSpan = writeSpan[MagicHeaderBytes.Length..];

        BinaryPrimitives.WriteInt32LittleEndian(writeSpan, SecureStorageConstants.Header.CURRENT_VERSION);
        writeSpan = writeSpan[sizeof(int)..];

        WriteSegment(container.Salt, ref writeSpan);
        WriteSegment(container.Nonce, ref writeSpan);
        WriteSegment(container.Tag, ref writeSpan);
        WriteSegment(container.AssociatedData, ref writeSpan);
        WriteSegment(container.Ciphertext, ref writeSpan);

        return buffer;
    }

    private static SecureContainer ParseSecureContainer(byte[] containerBytes)
    {
        ReadOnlySpan<byte> readSpan = containerBytes.AsSpan();

        if (readSpan.Length < MagicHeaderBytes.Length + sizeof(int))
        {
            throw new InvalidOperationException(
                ApplicationErrorMessages.SecureProtocolStateStorage.INVALID_CONTAINER_FORMAT);
        }

        if (!readSpan[..MagicHeaderBytes.Length].SequenceEqual(MagicHeaderBytes))
        {
            throw new InvalidOperationException(
                ApplicationErrorMessages.SecureProtocolStateStorage.INVALID_CONTAINER_FORMAT);
        }

        readSpan = readSpan[MagicHeaderBytes.Length..];

        int version = BinaryPrimitives.ReadInt32LittleEndian(readSpan);
        if (version != SecureStorageConstants.Header.CURRENT_VERSION)
        {
            throw new InvalidOperationException(
                string.Format(ApplicationErrorMessages.SecureProtocolStateStorage.UNSUPPORTED_VERSION, version));
        }

        readSpan = readSpan[sizeof(int)..];

        byte[] salt = ReadSegment(ref readSpan);
        byte[] nonce = ReadSegment(ref readSpan);
        byte[] tag = ReadSegment(ref readSpan);
        byte[] associatedData = ReadSegment(ref readSpan);
        byte[] ciphertext = ReadSegment(ref readSpan);

        if (!readSpan.IsEmpty)
        {
            throw new InvalidOperationException(
                ApplicationErrorMessages.SecureProtocolStateStorage.INVALID_CONTAINER_FORMAT);
        }

        return new SecureContainer(salt, nonce, tag, ciphertext, associatedData);
    }

    private async Task<byte[]> AddTamperProtectionAsync(byte[] data)
    {
        byte[] hmacKey = await GetHmacKeyAsync().ConfigureAwait(false);
        using HMACSHA512 hmac = new(hmacKey);
        byte[] mac = hmac.ComputeHash(data);

        byte[] result = new byte[data.Length + mac.Length];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);
        Buffer.BlockCopy(mac, 0, result, data.Length, mac.Length);

        ZeroBuffer(mac);
        return result;
    }

    private async Task<Result<byte[], SecureStorageFailure>> VerifyTamperProtectionAsync(byte[] protectedData)
    {
        if (protectedData.Length < SecureStorageConstants.Encryption.HMAC_SHA_512_SIZE)
        {
            return Result<byte[], SecureStorageFailure>.Err(
                new SecureStorageFailure(
                    ApplicationErrorMessages.SecureProtocolStateStorage.TAMPERED_STATE_DETECTED));
        }

        int macSize = SecureStorageConstants.Encryption.HMAC_SHA_512_SIZE;
        int dataLength = protectedData.Length - macSize;

        byte[] data = new byte[dataLength];
        byte[] mac = new byte[macSize];

        Buffer.BlockCopy(protectedData, 0, data, 0, dataLength);
        Buffer.BlockCopy(protectedData, dataLength, mac, 0, macSize);

        byte[] hmacKey = await GetHmacKeyAsync().ConfigureAwait(false);
        using HMACSHA512 hmac = new(hmacKey);
        byte[] expectedMac = hmac.ComputeHash(data);

        bool matches = CryptographicOperations.FixedTimeEquals(mac, expectedMac);

        ZeroBuffer(mac);
        ZeroBuffer(expectedMac);

        if (matches)
        {
            return Result<byte[], SecureStorageFailure>.Ok(data);
        }

        ZeroBuffer(data);
        return Result<byte[], SecureStorageFailure>.Err(
            new SecureStorageFailure(
                ApplicationErrorMessages.SecureProtocolStateStorage.TAMPERED_STATE_DETECTED));
    }

    private async Task<byte[]> GetHmacKeyAsync()
    {
        byte[]? cached = Volatile.Read(ref _cachedHmacKey);
        if (cached != null)
        {
            return cached;
        }

        byte[] newKey = await _platformProvider.GetOrCreateHmacKeyAsync().ConfigureAwait(false);
        byte[]? existing = Interlocked.CompareExchange(ref _cachedHmacKey, newKey, null);

        if (existing != null)
        {
            ZeroBuffer(newKey);
            return existing;
        }

        return newKey;
    }

    private static async Task<Option<byte[]>> ReadSecureFileAsync(string filePath)
    {
        return File.Exists(filePath)
            ? Option<byte[]>.Some(await File.ReadAllBytesAsync(filePath).ConfigureAwait(false))
            : Option<byte[]>.None;
    }

    private static async Task WriteSecureFileAsync(byte[] data, string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            DirectoryInfo directoryInfo = Directory.CreateDirectory(directory);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(directoryInfo.FullName,
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

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (!File.Exists(tempPath))
            {
                throw;
            }

            try
            {
                File.Delete(tempPath);
            }
            catch
            {
                // best-effort cleanup
            }

            throw;
        }
    }

    private static async Task DeleteFileIfExistsAsync(string filePath, int maxRetries = 3)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(filePath);
                if (attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
                }

                File.Delete(filePath);
                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                await Task.Delay(RetryDelay(attempt)).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                await Task.Delay(RetryDelay(attempt)).ConfigureAwait(false);
            }
        }

        if (!File.Exists(filePath))
        {
            return;
        }

        FileAttributes finalAttributes = File.GetAttributes(filePath);
        if (finalAttributes.HasFlag(FileAttributes.ReadOnly))
        {
            File.SetAttributes(filePath, finalAttributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(filePath);
    }

    private async Task<Option<byte[]>> TryGetStoredKeyAsync(string keychainKey)
    {
        byte[]? key = await _platformProvider.GetKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);
        return Option<byte[]>.From(key);
    }

    private async Task CleanupFailedSaveAsync(string storagePath, string keychainKey)
    {
        try
        {
            await DeleteFileIfExistsAsync(storagePath).ConfigureAwait(false);
        }
        catch
        {
            // best-effort cleanup
        }

        try
        {
            await _platformProvider.DeleteKeyFromKeychainAsync(keychainKey).ConfigureAwait(false);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    private async Task<(byte[] Key, byte[] Salt)> DeriveKeyAsync(byte[] membershipId)
    {
        byte[] salt = await _platformProvider.GenerateSecureRandomAsync(SecureStorageConstants.Encryption.SALT_SIZE)
            .ConfigureAwait(false);

        (byte[] key, byte[] _) = await DeriveKeyWithSaltAsync(membershipId, salt).ConfigureAwait(false);
        return (key, salt);
    }

    private async Task<(byte[] Key, byte[] Salt)> DeriveKeyWithSaltAsync(byte[] membershipId, byte[] salt)
    {
        using Argon2id argon2 = new(membershipId)
        {
            Salt = salt,
            DegreeOfParallelism = SecureStorageConstants.Argon2.PARALLELISM,
            Iterations = SecureStorageConstants.Argon2.ITERATIONS,
            MemorySize = SecureStorageConstants.Argon2.MEMORY_SIZE,
            AssociatedData = _deviceId
        };

        byte[] key = await argon2.GetBytesAsync(SecureStorageConstants.Encryption.KEY_SIZE).ConfigureAwait(false);
        return (key, salt);
    }

    private void EnsureSecureDirectoryExists()
    {
        DirectoryInfo directory = Directory.CreateDirectory(_storageDirectory);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(directory.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private bool TryEnsureNotDisposed(out SecureStorageFailure? failure)
    {
        if (_disposed)
        {
            failure = new SecureStorageFailure(ApplicationErrorMessages.SecureProtocolStateStorage.STORAGE_DISPOSED);
            return false;
        }

        failure = null;
        return true;
    }

    private static void WriteSegment(ReadOnlySpan<byte> value, ref Span<byte> destination)
    {
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.Length);
        destination = destination[sizeof(int)..];

        value.CopyTo(destination);
        destination = destination[value.Length..];
    }

    private static byte[] ReadSegment(ref ReadOnlySpan<byte> source)
    {
        if (source.Length < sizeof(int))
        {
            throw new InvalidOperationException(
                ApplicationErrorMessages.SecureProtocolStateStorage.INVALID_CONTAINER_FORMAT);
        }

        int length = BinaryPrimitives.ReadInt32LittleEndian(source);
        if (length < 0)
        {
            throw new InvalidOperationException(
                ApplicationErrorMessages.SecureProtocolStateStorage.INVALID_CONTAINER_FORMAT);
        }

        source = source[sizeof(int)..];
        if (source.Length < length)
        {
            throw new InvalidOperationException(
                ApplicationErrorMessages.SecureProtocolStateStorage.INVALID_CONTAINER_FORMAT);
        }

        byte[] result = source[..length].ToArray();
        source = source[length..];
        return result;
    }

    private static int SegmentSize(int payloadLength) => sizeof(int) + payloadLength;

    private static TimeSpan RetryDelay(int attempt) => TimeSpan.FromMilliseconds(100 * (attempt + 1));

    private static void ZeroBuffer(byte[]? buffer)
    {
        if (buffer is null)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(buffer);
    }

    private readonly record struct SecureContainer(
        byte[] Salt,
        byte[] Nonce,
        byte[] Tag,
        byte[] Ciphertext,
        byte[] AssociatedData);
}

public record SecureStorageFailure(string Message) : FailureBase(Message)
{
    public override object ToStructuredLog() => new { Message, Type = "SecureStorageFailure" };

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18nKeys.INTERNAL);
}
