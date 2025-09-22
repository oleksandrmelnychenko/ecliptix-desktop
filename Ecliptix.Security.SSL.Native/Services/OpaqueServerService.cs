using System;
using System.Runtime.InteropServices;
using Ecliptix.Utilities;

namespace Ecliptix.Security.SSL.Native.Services;

internal static class NativeOpaqueServer
{
    private const string LibraryName = "opaque_server";

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_server_keypair_generate(out IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void opaque_server_keypair_destroy(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_server_keypair_get_public_key(
        IntPtr handle, byte[] publicKey, UIntPtr keyBufferSize);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_server_create(IntPtr keypairHandle, out IntPtr serverHandle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void opaque_server_destroy(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_server_create_registration_response(
        IntPtr serverHandle, byte[] requestData, UIntPtr requestLength,
        byte[] responseData, UIntPtr responseBufferSize,
        byte[] credentialsData, UIntPtr credentialsBufferSize);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_credential_store_create(out IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void opaque_credential_store_destroy(IntPtr handle);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_credential_store_store(
        IntPtr storeHandle, byte[] userId, UIntPtr userIdLength,
        byte[] credentialsData, UIntPtr credentialsLength);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int opaque_credential_store_retrieve(
        IntPtr storeHandle, byte[] userId, UIntPtr userIdLength,
        byte[] credentialsData, UIntPtr credentialsBufferSize);

    public const int PUBLIC_KEY_LENGTH = 32;
    public const int REGISTRATION_REQUEST_LENGTH = 32;
    public const int REGISTRATION_RESPONSE_LENGTH = 96;
    public const int ENVELOPE_LENGTH = 96;
    public const int PRIVATE_KEY_LENGTH = 32;
    public const int SERVER_CREDENTIALS_LENGTH = ENVELOPE_LENGTH + PRIVATE_KEY_LENGTH;
}

public enum OpaqueResult
{
    Success = 0,
    InvalidInput = -1,
    CryptoError = -2,
    MemoryError = -3,
    ValidationError = -4,
    AuthenticationError = -5
}

public sealed class OpaqueServerFailure
{
    public string Message { get; }

    private OpaqueServerFailure(string message)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    public static OpaqueServerFailure InvalidInput(string message) => new($"Invalid input: {message}");
    public static OpaqueServerFailure CryptoError(string message) => new($"Cryptographic error: {message}");
    public static OpaqueServerFailure AuthenticationFailed(string message) => new($"Authentication failed: {message}");
}

public sealed class OpaqueServerKeyPair : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public OpaqueServerKeyPair()
    {
        int result = NativeOpaqueServer.opaque_server_keypair_generate(out _handle);
        if (result != (int)OpaqueResult.Success || _handle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to generate server keypair: {result}");
    }

    public byte[] GetPublicKey()
    {
        ThrowIfDisposed();

        byte[] publicKey = new byte[NativeOpaqueServer.PUBLIC_KEY_LENGTH];
        int result = NativeOpaqueServer.opaque_server_keypair_get_public_key(
            _handle, publicKey, (UIntPtr)publicKey.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to get public key: {result}");

        return publicKey;
    }

    internal IntPtr Handle
    {
        get
        {
            ThrowIfDisposed();
            return _handle;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpaqueServerKeyPair));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != IntPtr.Zero)
                NativeOpaqueServer.opaque_server_keypair_destroy(_handle);
            _disposed = true;
        }
    }
}

public sealed class OpaqueNativeServer : IDisposable
{
    private readonly IntPtr _serverHandle;
    private bool _disposed;

    public OpaqueNativeServer(OpaqueServerKeyPair keyPair)
    {
        if (keyPair == null)
            throw new ArgumentNullException(nameof(keyPair));

        int result = NativeOpaqueServer.opaque_server_create(keyPair.Handle, out _serverHandle);
        if (result != (int)OpaqueResult.Success || _serverHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create OPAQUE server: {result}");
    }

    public (byte[] RegistrationResponse, byte[] ServerCredentials) CreateRegistrationResponse(byte[] registrationRequest)
    {
        ThrowIfDisposed();

        if (registrationRequest == null)
            throw new ArgumentNullException(nameof(registrationRequest));
        if (registrationRequest.Length != NativeOpaqueServer.REGISTRATION_REQUEST_LENGTH)
            throw new ArgumentException($"Invalid registration request length: expected {NativeOpaqueServer.REGISTRATION_REQUEST_LENGTH}, got {registrationRequest.Length}");

        byte[] response = new byte[NativeOpaqueServer.REGISTRATION_RESPONSE_LENGTH];
        byte[] credentials = new byte[NativeOpaqueServer.SERVER_CREDENTIALS_LENGTH];

        int result = NativeOpaqueServer.opaque_server_create_registration_response(
            _serverHandle,
            registrationRequest,
            (UIntPtr)registrationRequest.Length,
            response,
            (UIntPtr)response.Length,
            credentials,
            (UIntPtr)credentials.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Registration response creation failed: {result}");

        return (response, credentials);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpaqueNativeServer));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_serverHandle != IntPtr.Zero)
                NativeOpaqueServer.opaque_server_destroy(_serverHandle);
            _disposed = true;
        }
    }
}

public sealed class OpaqueCredentialStore : IDisposable
{
    private readonly IntPtr _handle;
    private bool _disposed;

    public OpaqueCredentialStore()
    {
        int result = NativeOpaqueServer.opaque_credential_store_create(out _handle);
        if (result != (int)OpaqueResult.Success || _handle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create credential store: {result}");
    }

    public void StoreCredentials(string userId, byte[] credentials)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        if (credentials == null)
            throw new ArgumentNullException(nameof(credentials));

        byte[] userIdBytes = System.Text.Encoding.UTF8.GetBytes(userId);

        int result = NativeOpaqueServer.opaque_credential_store_store(
            _handle,
            userIdBytes,
            (UIntPtr)userIdBytes.Length,
            credentials,
            (UIntPtr)credentials.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to store credentials: {result}");
    }

    public byte[]? RetrieveCredentials(string userId)
    {
        ThrowIfDisposed();

        if (string.IsNullOrEmpty(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));

        byte[] userIdBytes = System.Text.Encoding.UTF8.GetBytes(userId);
        byte[] credentials = new byte[NativeOpaqueServer.SERVER_CREDENTIALS_LENGTH];

        int result = NativeOpaqueServer.opaque_credential_store_retrieve(
            _handle,
            userIdBytes,
            (UIntPtr)userIdBytes.Length,
            credentials,
            (UIntPtr)credentials.Length);

        if (result == (int)OpaqueResult.InvalidInput)
            return null;

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to retrieve credentials: {result}");

        return credentials;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpaqueCredentialStore));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_handle != IntPtr.Zero)
                NativeOpaqueServer.opaque_credential_store_destroy(_handle);
            _disposed = true;
        }
    }
}

/// <summary>
/// High-level OPAQUE server service for ASP.NET Core integration.
/// Provides secure password authentication without storing plaintext passwords.
/// </summary>
public sealed class OpaqueServerService : IDisposable
{
    private readonly OpaqueServerKeyPair _keyPair;
    private readonly OpaqueNativeServer _server;
    private readonly OpaqueCredentialStore _credentialStore;

    public OpaqueServerService()
    {
        _keyPair = new OpaqueServerKeyPair();
        _server = new OpaqueNativeServer(_keyPair);
        _credentialStore = new OpaqueCredentialStore();
    }

    /// <summary>
    /// Gets the server's public key for client initialization.
    /// </summary>
    public byte[] GetServerPublicKey() => _keyPair.GetPublicKey();

    /// <summary>
    /// Processes a user registration request and stores credentials securely.
    /// </summary>
    public Result<(byte[] RegistrationResponse, string UserId), OpaqueServerFailure> ProcessRegistration(
        string userId, byte[] registrationRequest)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
                return Result<(byte[], string), OpaqueServerFailure>.Err(
                    OpaqueServerFailure.InvalidInput("User ID cannot be null or empty"));

            var (response, credentials) = _server.CreateRegistrationResponse(registrationRequest);
            _credentialStore.StoreCredentials(userId, credentials);

            return Result<(byte[], string), OpaqueServerFailure>.Ok((response, userId));
        }
        catch (Exception ex)
        {
            return Result<(byte[], string), OpaqueServerFailure>.Err(
                OpaqueServerFailure.CryptoError($"Registration failed: {ex.Message}"));
        }
    }

    /// <summary>
    /// Validates if a user exists in the credential store.
    /// </summary>
    public bool UserExists(string userId)
    {
        if (string.IsNullOrEmpty(userId))
            return false;

        try
        {
            return _credentialStore.RetrieveCredentials(userId) != null;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _credentialStore?.Dispose();
        _server?.Dispose();
        _keyPair?.Dispose();
    }
}