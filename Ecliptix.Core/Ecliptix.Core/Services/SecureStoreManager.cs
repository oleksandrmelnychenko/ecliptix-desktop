using System;
using System.IO;
using System.Linq;
using System.Threading;
using Ecliptix.Core.Protocol.Utilities;
using NeoSmart.SecureStore;

namespace Ecliptix.Core.Services;

public sealed class SecureStoreManager : IDisposable
{
    private readonly SecretsManager _secretsManager;
    private string? _storeFilePath;
    private readonly Lock _lock = new();
    private bool _isDisposed;

    private SecureStoreManager(SecretsManager manager, string? storeFilePath = null)
    {
        _secretsManager = manager;
        _storeFilePath = storeFilePath;
    }

    public static SecureStoreManager CreateNew()
    {
        SecretsManager manager = SecretsManager.CreateStore();
        manager.GenerateKey();
        return new SecureStoreManager(manager);
    }

    public static SecureStoreManager LoadFromKeyFile(string storeFilePath, string keyFilePath)
    {
        if (!File.Exists(storeFilePath))
            throw new FileNotFoundException("Secure store file not found.", storeFilePath);
        if (!File.Exists(keyFilePath))
            throw new FileNotFoundException("Key file not found.", keyFilePath);

        try
        {
            var manager = SecretsManager.LoadStore(storeFilePath);
            manager.LoadKeyFromFile(keyFilePath);
            return new SecureStoreManager(manager, storeFilePath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Failed to load secure store from '{storeFilePath}'. The file may be corrupt or the key is incorrect.",
                ex);
        }
    }

    public static SecureStoreManager LoadFromPassword(string storeFilePath, string password)
    {
        if (!File.Exists(storeFilePath))
            throw new FileNotFoundException("Secure store file not found.", storeFilePath);
        if (string.IsNullOrEmpty(password))
            throw new ArgumentNullException(nameof(password));

        try
        {
            SecretsManager manager = SecretsManager.LoadStore(storeFilePath);
            manager.LoadKeyFromPassword(password);
            return new SecureStoreManager(manager, storeFilePath);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException(
                $"Failed to load secure store from '{storeFilePath}'. The file may be corrupt or the password is incorrect.",
                ex);
        }
    }

    public void Set<T>(string key, T value)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
        ThrowIfDisposed();
        lock (_lock)
        {
            _secretsManager.Set(key, value);
        }
    }

    public Result<Option<byte[]>, InternalServiceApiFailure> TryGet(string key) =>
        Result<Option<byte[]>, InternalServiceApiFailure>.Try(() =>
        {
            lock (_lock)
            {
                if (!_secretsManager.TryGetBytes(key, out SecureBuffer secureBuffer)) return Option<byte[]>.None;

                using (secureBuffer)
                {
                    return Option<byte[]>.Some(secureBuffer.Buffer.ToArray());
                }
            }
        }, err => InternalServiceApiFailure.SecureStoreAccessDenied(err.Message));


    public Result<bool, InternalServiceApiFailure> Delete(string key) =>
        Result<bool, InternalServiceApiFailure>.Try(() =>
        {
            lock (_lock)
            {
                return _secretsManager.Delete(key);
            }
        }, err => InternalServiceApiFailure.SecureStoreAccessDenied(err.Message));

    public void Save(string? path = null)
    {
        ThrowIfDisposed();
        string? savePath = path ?? _storeFilePath;

        if (string.IsNullOrEmpty(savePath))
        {
            throw new InvalidOperationException(
                "Cannot save the store without providing a file path, as it was not loaded from a file.");
        }

        lock (_lock)
        {
            string tempPath = savePath + ".tmp";
            _secretsManager.SaveStore(tempPath);
            File.Move(tempPath, savePath, overwrite: true);

            if (path != null)
            {
                _storeFilePath = path;
            }
        }
    }

    public void ExportKey(string keyFilePath)
    {
        if (string.IsNullOrEmpty(keyFilePath)) throw new ArgumentNullException(nameof(keyFilePath));
        ThrowIfDisposed();
        _secretsManager.ExportKey(keyFilePath);
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _secretsManager?.Dispose();
            _isDisposed = true;
        }
    }
}