using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Services;

public sealed class SecureStorageProvider : ISecureStorageProvider
{
    private readonly IDataProtector _protector;
    private readonly string _encryptedStatePath;
    private readonly ILogger<SecureStorageProvider> _logger;
    private bool _disposed;

    public SecureStorageProvider(
        IOptions<SecureStoreOptions> options,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SecureStorageProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SecureStoreOptions opts = options?.Value ?? throw new ArgumentNullException(nameof(options));
        opts.Validate();

        _encryptedStatePath = opts.EncryptedStatePath;
        _protector = dataProtectionProvider.CreateProtector("Ecliptix.SecureStorage");

        try
        {
            EnsureDirectoryExists(_encryptedStatePath);
            _logger.LogDebug("Initialized SecureStorageProvider with state path {Path}", _encryptedStatePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize secure storage at {Path}", _encryptedStatePath);
            throw new InvalidOperationException($"Could not initialize secure storage at {_encryptedStatePath}.", ex);
        }
    }

    public async Task<bool> StoreAsync(string key, byte[] data)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecureStorageProvider));
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogError("Storage key cannot be null or empty");
            return false;
        }

        if (data == null)
        {
            _logger.LogError("Data to store cannot be null for key {Key}", key);
            return false;
        }

        try
        {
            var protectedData = _protector.Protect(data);
            var filePath = GetFilePath(key);
            await File.WriteAllBytesAsync(filePath, protectedData);
            SetSecurePermissions(filePath);
            _logger.LogDebug("Stored data for key {Key} at {Path}", key, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store data for key {Key} at {Path}", key, GetFilePath(key));
            return false;
        }
    }

    public async Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecureStorageProvider));
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogError("Storage key cannot be null or empty");
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound("Storage key cannot be null or empty."));
        }

        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("No data found for key {Key} at {Path}", key, filePath);
                return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.None);
            }

            var protectedData = await File.ReadAllBytesAsync(filePath);
            var data = _protector.Unprotect(protectedData);
            _logger.LogDebug("Retrieved data for key {Key} from {Path}", key, filePath);
            return Result<Option<byte[]>, InternalServiceApiFailure>.Ok(Option<byte[]>.Some(data));
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex, "Failed to decrypt data for key {Key} at {Path}", key, GetFilePath(key));
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreNotFound(
                    "Failed to decrypt data. The key may not exist or the data may be corrupted."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve data for key {Key} from {Path}", key, GetFilePath(key));
            return Result<Option<byte[]>, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreAccessDenied("Failed to access secure storage.", ex));
        }
    }

    public async Task<bool> DeleteAsync(string key)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SecureStorageProvider));
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogError("Storage key cannot be null or empty");
            return false;
        }

        try
        {
            var filePath = GetFilePath(key);
            if (!File.Exists(filePath))
            {
                _logger.LogDebug("No data to delete for key {Key} at {Path}", key, filePath);
                return false;
            }

            File.Delete(filePath);
            _logger.LogDebug("Deleted data for key {Key} from {Path}", key, filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete data for key {Key} from {Path}", key, GetFilePath(key));
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _logger.LogDebug("SecureStorageProvider disposed");
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    private string GetFilePath(string key) => Path.Combine(_encryptedStatePath, $"{key}.enc");

    private static void EnsureDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
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
                    _logger.LogDebug("Set secure permissions (600) on {Path}", path);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to set permissions for {Path}; will retry on file creation", path);
            }
        }
    }
}