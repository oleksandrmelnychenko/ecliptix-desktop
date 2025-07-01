using System;
using System.IO;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Services;

public sealed class SecureSecureStorageProvider : ISecureStorageProvider
{
    private readonly SecureStoreManager _secureStore;
    private readonly string _storePath;
    private bool _disposed;

    public SecureSecureStorageProvider(
        IOptions<SecureStoreOptions> options)
    {
        _storePath = options.Value.StorePath;
        string keyPath = options.Value.KeyPath;
        string password = options.Value.Password;
        bool usePassword = options.Value.UsePassword;

        try
        {
            string? storeDir = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(storeDir) && !Directory.Exists(storeDir))
            {
                Directory.CreateDirectory(storeDir);
            }

            if (File.Exists(_storePath))
            {
                _secureStore = usePassword
                    ? SecureStoreManager.LoadFromPassword(_storePath, password)
                    : SecureStoreManager.LoadFromKeyFile(_storePath, keyPath);
            }
            else
            {
                _secureStore = SecureStoreManager.CreateNew();
                _secureStore.Save(_storePath);
                if (!usePassword)
                {
                    string? keyDir = Path.GetDirectoryName(keyPath);
                    if (!string.IsNullOrEmpty(keyDir) && !Directory.Exists(keyDir))
                    {
                        Directory.CreateDirectory(keyDir);
                    }

                    _secureStore.ExportKey(keyPath);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Could not initialize secure storage provider.", ex);
        }
    }

    public bool Store(string key, byte[] data)
    {
        try
        {
            _secureStore.Set(key, data);
            _secureStore.Save();
            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public Result<Option<byte[]>, InternalServiceApiFailure> TryGetByKey(string key) =>
        _secureStore.TryGet(key);

    public Result<bool, InternalServiceApiFailure> Delete(string key)
    {
        Result<bool, InternalServiceApiFailure> deleteOperationResult = _secureStore.Delete(key);

        if (deleteOperationResult.IsOk)
        {
            _secureStore.Save();
        }

        return deleteOperationResult;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await Task.Run(() => _secureStore?.Dispose());
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}