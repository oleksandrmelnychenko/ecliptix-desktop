using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ecliptix.Core.Services
{
    public class SecureByteStorageService : IBytesStorageService, IDisposable
    {
        private readonly ILogger<SecureByteStorageService> _logger;
        private readonly SecureStoreManager _secureStore;
        private readonly string _storePath;
        private readonly string _keyPath;
        private bool _disposed;

          public SecureByteStorageService(ILogger<SecureByteStorageService> logger,
                                   IOptions<SecureStoreOptions> options)
    {
        _logger = logger;
        _storePath = options.Value.StorePath;
        _keyPath = options.Value.KeyPath;

        try
        {
            // Create directories if they don't exist
            string storeDir = Path.GetDirectoryName(_storePath);
            string keyDir = Path.GetDirectoryName(_keyPath);
            
            if (!string.IsNullOrEmpty(storeDir) && !Directory.Exists(storeDir))
                Directory.CreateDirectory(storeDir);
                
            if (!string.IsNullOrEmpty(keyDir) && !Directory.Exists(keyDir))
                Directory.CreateDirectory(keyDir);

            if (File.Exists(_storePath) && File.Exists(_keyPath))
            {
                _logger.LogInformation("Loading existing secure store from {StorePath}", _storePath);
                _secureStore = new SecureStoreManager(_storePath, _keyPath, StoreLoadMethod.ByKeyFile);
            }
            else
            {
                _logger.LogInformation("Creating new secure store at {StorePath}", _storePath);
                _secureStore = SecureStoreManager.CreateNewStore();
                _secureStore.SaveStore(_storePath);
                _secureStore.ExportKey(_keyPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize secure storage at {StorePath}", _storePath);
            throw;
        }
    }

    public Task<bool> StoreAsync(string key, byte[] data)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));
        
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        try
        {
            _logger.LogInformation("Storing {Length} bytes of data with key {Key}", data.Length, key);
            _secureStore.Set(key, data);
            _secureStore.SaveStore(_storePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Failed to store data for key {Key}", key);
            return Task.FromResult(false);
        }
    }

        public Task<byte[]?> RetrieveAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            try
            {
                byte[]? data = _secureStore.GetBytes(key);
                return Task.FromResult(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve data for key {Key}", key);
                return Task.FromResult<byte[]?>(null);
            }
        }

        public Task<bool> DeleteAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            try
            {
                // The SecureStoreManager doesn't have a direct delete method,
                // so we'll set the value to null/empty and save
                _secureStore.Set(key, Array.Empty<byte>());
                _secureStore.SaveStore(_storePath);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete data for key {Key}", key);
                return Task.FromResult(false);
            }
        }

        public Task ClearAllAsync()
        {
            try
            {
                // Create a fresh store to clear all data
                _secureStore.Dispose();
                
                // Delete the existing store file
                if (File.Exists(_storePath))
                    File.Delete(_storePath);
                
                // Create a new empty store
                var newStore = SecureStoreManager.CreateNewStore();
                newStore.SaveStore(_storePath);
                newStore.ExportKey(_keyPath);
                
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear all data");
                return Task.CompletedTask;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                _secureStore?.Dispose();
            }

            _disposed = true;
        }
    }

    public class SecureStoreOptions
    {
        public string StorePath { get; set; } = "secure_bytes.bin";
        public string KeyPath { get; set; } = "secure_bytes.key";
        public string Password { get; set; } = string.Empty;
        public bool UsePassword { get; set; } = false;
    }
}