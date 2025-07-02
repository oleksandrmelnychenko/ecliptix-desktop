using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace Ecliptix.Core.Services;

public sealed class SecureStorageProvider : ISecureStorageProvider
{
    private readonly IDataProtector _protector;
    private readonly string _encryptedStatePath;
    private bool _disposed;

    public SecureStorageProvider(
        IOptions<SecureStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider)
    {
        SecureStoreOptions opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        opts.Validate();

        _encryptedStatePath = opts.EncryptedStatePath;
        _protector = dataProtectionProvider.CreateProtector("Ecliptix.SecureStorage");

        try
        {
            EnsureDirectoryExists(_encryptedStatePath);
            Log.Debug("Initialized SecureStorageProvider with state path {Path}", _encryptedStatePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize secure storage at {Path}", _encryptedStatePath);
            throw new InvalidOperationException($"Could not initialize secure storage at {_encryptedStatePath}.", ex);
        }
    }

    public async Task<bool> StoreAsync(string key, byte[] data)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecureStorageProvider));
        if (string.IsNullOrEmpty(key))
        {
            Log.Error("Storage key cannot be null or empty");
            return false;
        }

        if (data == null)
        {
            Log.Error("Data to store cannot be null for key {Key}", key);
            return false;
        }

        try
        {
            var protectedData = _protector.Protect(data);
            var filePath = GetFilePath(key);

            // Перевіряємо та створюємо директорію
            EnsureDirectoryExists(filePath);
            Log.Debug("Ensured directory exists for {Path}", Path.GetDirectoryName(filePath));

            await File.WriteAllBytesAsync(filePath, protectedData);
            SetSecurePermissions(filePath);
            Log.Debug("Stored data for key {Key} at {Path}", key, filePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to store data for key {Key} at {Path}", key, GetFilePath(key));
            return false;
        }
    }

    public async Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecureStorageProvider));
        if (string.IsNullOrEmpty(key))
        {
            Log.Error("Storage key cannot be null or empty");
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound("Storage key cannot be null or empty."));
        }

        string filePath = GetFilePath(key);

        try
        {
            if (!File.Exists(filePath))
            {
                Log.Debug("No data found for key {Key} at {Path}", key, filePath);
                return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
            }

            Log.Debug("Starting read for key {Key} at {Path}", key, filePath);
            byte[] protectedData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            if (protectedData.Length == 0)
            {
                Log.Warning("Empty or null protected data read for key {Key} at {Path}", key, filePath);
                return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                    InternalServiceApiFailure.SecureStoreNotFound("No valid data to decrypt."));
            }

            Log.Debug("Attempting to unprotect {Length} bytes for key {Key} from {Path}", protectedData.Length, key,
                filePath);
            byte[] data = _protector.Unprotect(protectedData);
            Log.Debug("Retrieved data for key {Key} from {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.Some(data));
        }
        catch (CryptographicException ex)
        {
            Log.Error(ex, "Failed to decrypt data for key {Key} at {Path}. Error: {ErrorMessage}", key, filePath,
                ex.Message);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreNotFound(
                    $"Failed to decrypt data: {ex.Message}. The key may not exist or the data may be corrupted."));
        }
        catch (IOException ex)
        {
            Log.Error(ex, "Failed to read file for key {Key} at {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to access secure storage.", ex));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error reading file for key {Key} at {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Unexpected error reading file.", ex));
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecureStorageProvider));
        if (string.IsNullOrEmpty(key))
        {
            Log.Error("Storage key cannot be null or empty");
            return false;
        }

        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                Log.Debug("No data to delete for key {Key} at {Path}", key, filePath);
                return false;
            }

            File.Delete(filePath);
            Log.Debug("Deleted data for key {Key} from {Path}", key, filePath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete data for key {Key} from {Path}", key, GetFilePath(key));
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            Log.Debug("SecureStorageProvider disposed");
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    private string GetFilePath(string key) => Path.Combine(_encryptedStatePath, $"{key}.enc");

    private static void EnsureDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Log.Information("Created directory: {Directory}", directory, 0);
            }
        }
    }

    private void SetSecurePermissions(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    Log.Debug("Set secure permissions (600) on {Path}", path);
                }
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Failed to set permissions for {Path}; will retry on file creation", path);
            }
        }
    }
}