using System;
using System.IO;
using System.Text;
using NeoSmart.SecureStore; // Make sure you have this library referenced in your project

/// <summary>
/// Specifies the method to use when loading an existing secure store.
/// </summary>
public enum StoreLoadMethod
{
    /// <summary>
    /// Load the store using a separate key file.
    /// </summary>
    ByKeyFile,

    /// <summary>
    /// Load the store by deriving the key from a password.
    /// </summary>
    ByPassword
}

/// <summary>
/// Manages secure storage of sensitive data using the NeoSmart SecureStore library.
/// Provides a simple interface for storing and retrieving encrypted data with support
/// for both key file and password-based encryption.
/// </summary>
/// <remarks>
/// This class implements IDisposable and should be used within a using statement
/// to ensure proper resource cleanup.
/// 
/// Common usage patterns:
/// 
/// 1. Creating and saving a new store:
/// <code>
/// using (var store = SecureStoreManager.CreateNewStore())
/// {
///     store.Set("api_key", "my-secret-key");
///     store.SaveStore("secrets.bin");
///     store.ExportKey("key.bin");
/// }
/// </code>
/// 
/// 2. Loading an existing store with a key file:
/// <code>
/// using (var store = new SecureStoreManager(
///     "secrets.bin",
///     "key.bin",
///     StoreLoadMethod.ByKeyFile))
/// {
///     string apiKey = store.GetString("api_key");
/// }
/// </code>
/// 
/// 3. Loading with a password:
/// <code>
/// using (var store = new SecureStoreManager(
///     "secrets.bin",
///     "my-secure-password",
///     StoreLoadMethod.ByPassword))
/// {
///     store.Set("connection_string", "Server=myserver;Database=mydb;");
///     store.SaveStore("secrets.bin");
/// }
/// </code>
/// 
/// 4. Storing and retrieving binary data:
/// <code>
/// using (var store = SecureStoreManager.CreateNewStore())
/// {
///     byte[] certificate = File.ReadAllBytes("cert.pfx");
///     store.Set("ssl_cert", certificate);
///     
///     byte[] retrievedCert = store.GetBytes("ssl_cert");
/// }
/// </code>
/// 
/// Best Practices:
/// - Always use strong passwords or securely stored key files
/// - Keep key files separate from the store file
/// - Use using statements to ensure proper disposal
/// - Handle exceptions for file operations
/// - Backup your key files - losing them means losing access to the store
/// 
/// Security Considerations:
/// - The store file is encrypted and safe to store in version control
/// - Never store the key file together with the store file
/// - Use password-based encryption for development/testing
/// - Use key file-based encryption for production systems
/// </remarks>
public class SecureStoreManager : IDisposable
{
    private SecretsManager _secretsManager;
    private bool _isDisposed = false;
    private string _storeFilePath;

    /// <summary>
    /// Private constructor for internal use
    /// </summary>
    private SecureStoreManager()
    {
        _secretsManager = SecretsManager.CreateStore();
    }

    /// <summary>
    /// Initializes a new instance of the SecureStoreManager class.
    /// </summary>
    /// <param name="storeFilePath">The path to the secure store file (.bin).</param>
    /// <param name="keyOrPassword">The path to the key file if loadMethod is ByKeyFile, or the password if loadMethod is ByPassword.</param>
    /// <param name="loadMethod">Specifies whether to load using a key file or a password.</param>
    /// <exception cref="FileNotFoundException">Thrown if the store file or key file is not found.</exception>
    /// <exception cref="ArgumentNullException">Thrown if the password is null or empty when loadMethod is ByPassword.</exception>
    /// <exception cref="ArgumentException">Thrown for invalid loadMethod.</exception>
    public SecureStoreManager(string storeFilePath, string keyOrPassword, StoreLoadMethod loadMethod)
    {
        if (!File.Exists(storeFilePath))
        {
            throw new FileNotFoundException($"Secure store file not found: {storeFilePath}");
        }

        _storeFilePath = storeFilePath;
        _secretsManager = SecretsManager.CreateStore();

        switch (loadMethod)
        {
            case StoreLoadMethod.ByKeyFile:
                LoadByKeyFile(keyOrPassword);
                break;
            case StoreLoadMethod.ByPassword:
                LoadByPassword(keyOrPassword);
                break;
            default:
                throw new ArgumentException("Invalid StoreLoadMethod specified.", nameof(loadMethod));
        }

        // Load the store content after the key is loaded/derived
        _secretsManager = SecretsManager.LoadStore(storeFilePath);
    }

    private void LoadByKeyFile(string keyFilePath)
    {
        if (!File.Exists(keyFilePath))
        {
            throw new FileNotFoundException($"Key file not found: {keyFilePath}");
        }
        _secretsManager.LoadKeyFromFile(keyFilePath);
    }

    private void LoadByPassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentNullException(nameof(password), "Password cannot be null or empty when loading by password.");
        }
        _secretsManager.LoadKeyFromPassword(password);
    }

    /// <summary>
    /// Creates a new SecureStoreManager instance with a newly generated key.
    /// This is a static factory method for creating a fresh store.
    /// </summary>
    /// <returns>A new SecureStoreManager instance.</returns>
    public static SecureStoreManager CreateNewStore()
    {
        var manager = new SecureStoreManager();
        manager._secretsManager.GenerateKey();
        return manager;
    }

    /// <summary>
    /// Sets a string value in the secure store.
    /// </summary>
    /// <param name="key">The key for the value.</param>
    /// <param name="value">The string value to store.</param>
    public void Set(string key, string value)
    {
        ThrowIfDisposed();
        _secretsManager.Set(key, value);
    }

    /// <summary>
    /// Sets a byte array value in the secure store.
    /// </summary>
    /// <param name="key">The key for the value.</param>
    /// <param name="value">The byte array value to store.</param>
    public void Set(string key, byte[] value)
    {
        ThrowIfDisposed();
        string base64Data = Convert.ToBase64String(value);
        _secretsManager.Set(key, base64Data);
    }

    /// <summary>
    /// Gets a string value from the secure store.
    /// </summary>
    /// <param name="key">The key for the value.</param>
    /// <returns>The string value, or null if the key is not found.</returns>
    public string GetString(string key)
    {
        ThrowIfDisposed();
        return _secretsManager.Get(key);
    }

    /// <summary>
    /// Gets a byte array value from the secure store.
    /// </summary>
    /// <param name="key">The key for the value.</param>
    /// <returns>The byte array value, or null if the key is not found.</returns>
    public byte[] GetBytes(string key)
    {
        ThrowIfDisposed();
        try
        {
            string base64Data = _secretsManager.Get(key);
            return Convert.FromBase64String(base64Data);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Saves the current state of the secure store to a file.
    /// </summary>
    /// <param name="storeFilePath">The path where the store will be saved.</param>
    public void SaveStore(string storeFilePath)
    {
        ThrowIfDisposed();
        _secretsManager.SaveStore(storeFilePath);
    }

    /// <summary>
    /// Exports the key used by the secure store to a file.
    /// This is useful if the store was created with GenerateKey() or LoadKeyFromPassword()
    /// and you need to persist the key for later use with LoadFromKeyFile().
    /// </summary>
    /// <param name="keyFilePath">The path where the key will be saved.</param>
    public void ExportKey(string keyFilePath)
    {
        ThrowIfDisposed();
        _secretsManager.ExportKey(keyFilePath);
    }

    /// <summary>
    /// Throws an ObjectDisposedException if the object has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    #region IDisposable Support
    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing)
            {
                // Dispose managed state (managed objects)
                _secretsManager?.Dispose();
                _secretsManager = null;
            }

            // Free unmanaged resources (unmanaged objects) and override finalizer
            // Set large fields to null
            _isDisposed = true;
        }
    }

    // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~SecureStoreManager()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    #endregion
}
