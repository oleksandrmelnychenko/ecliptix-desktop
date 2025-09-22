using System;
using Ecliptix.Opaque.Protocol.Native;

namespace Ecliptix.Opaque.Protocol;

/// <summary>
/// Ultra-thin OPAQUE protocol client wrapper
/// </summary>
public sealed class OpaqueClient : IDisposable
{
    private readonly IntPtr _clientHandle;
    private bool _disposed;

    /// <summary>
    /// Creates new OPAQUE client with server public key
    /// </summary>
    /// <param name="serverPublicKey">Server's public key (32 bytes)</param>
    public OpaqueClient(byte[] serverPublicKey)
    {
        if (serverPublicKey?.Length != OpaqueConstants.PUBLIC_KEY_LENGTH)
            throw new ArgumentException($"Server public key must be {OpaqueConstants.PUBLIC_KEY_LENGTH} bytes");

        int result = OpaqueNative.opaque_client_create(serverPublicKey, (UIntPtr)serverPublicKey.Length, out _clientHandle);
        if (result != (int)OpaqueResult.Success || _clientHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create OPAQUE client: {(OpaqueResult)result}");
    }

    /// <summary>
    /// Creates registration request for new user account
    /// </summary>
    /// <param name="password">User password</param>
    /// <returns>Registration result with request data and state</returns>
    public RegistrationResult CreateRegistrationRequest(byte[] password)
    {
        ThrowIfDisposed();
        if (password == null || password.Length == 0) throw new ArgumentException("Password cannot be null or empty");

        try
        {
            byte[] request = new byte[OpaqueConstants.REGISTRATION_REQUEST_LENGTH];

            int stateResult = OpaqueNative.opaque_client_state_create(out IntPtr state);
            if (stateResult != (int)OpaqueResult.Success)
                throw new InvalidOperationException($"Failed to create state: {(OpaqueResult)stateResult}");

            int result = OpaqueNative.opaque_client_create_registration_request(
                _clientHandle, password, (UIntPtr)password.Length, state, request, (UIntPtr)request.Length);

            if (result != (int)OpaqueResult.Success)
            {
                OpaqueNative.opaque_client_state_destroy(state);
                throw new InvalidOperationException($"Failed to create registration request: {(OpaqueResult)result}");
            }

            return new RegistrationResult(request, state);
        }
        finally
        {
            ClearPassword(password);
        }
    }

    /// <summary>
    /// Finalizes registration with server response
    /// </summary>
    /// <param name="serverResponse">Server's registration response</param>
    /// <param name="registrationState">Registration state from CreateRegistrationRequest</param>
    /// <returns>Registration record to store on server</returns>
    public byte[] FinalizeRegistration(byte[] serverResponse, RegistrationResult registrationState)
    {
        ThrowIfDisposed();
        if (serverResponse?.Length != OpaqueConstants.REGISTRATION_RESPONSE_LENGTH)
            throw new ArgumentException($"Server response must be {OpaqueConstants.REGISTRATION_RESPONSE_LENGTH} bytes");

        byte[] record = new byte[OpaqueConstants.REGISTRATION_RECORD_LENGTH]; // Envelope + client public key

        int result = OpaqueNative.opaque_client_finalize_registration(
            _clientHandle, serverResponse, (UIntPtr)serverResponse.Length,
            registrationState.StateHandle, record, (UIntPtr)record.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to finalize registration: {(OpaqueResult)result}");

        return record;
    }

    /// <summary>
    /// Generates KE1 message for authentication
    /// </summary>
    /// <param name="password">User password</param>
    /// <returns>Key exchange result with KE1 data and state</returns>
    public KeyExchangeResult GenerateKE1(byte[] password)
    {
        ThrowIfDisposed();
        if (password == null || password.Length == 0) throw new ArgumentException("Password cannot be null or empty");

        try
        {
            byte[] ke1 = new byte[OpaqueConstants.KE1_LENGTH];

            int stateResult = OpaqueNative.opaque_client_state_create(out IntPtr state);
            if (stateResult != (int)OpaqueResult.Success)
                throw new InvalidOperationException($"Failed to create state: {(OpaqueResult)stateResult}");

            int result = OpaqueNative.opaque_client_generate_ke1(
                _clientHandle, password, (UIntPtr)password.Length, state, ke1, (UIntPtr)ke1.Length);

            if (result != (int)OpaqueResult.Success)
            {
                OpaqueNative.opaque_client_state_destroy(state);
                throw new InvalidOperationException($"Failed to generate KE1: {(OpaqueResult)result}");
            }

            return new KeyExchangeResult(ke1, state);
        }
        finally
        {
            ClearPassword(password);
        }
    }

    /// <summary>
    /// Generates KE3 message and completes authentication
    /// </summary>
    /// <param name="ke2">Server's KE2 message</param>
    /// <param name="keyExchangeState">State from GenerateKE1</param>
    /// <returns>KE3 message for server</returns>
    public byte[] GenerateKE3(byte[] ke2, KeyExchangeResult keyExchangeState)
    {
        ThrowIfDisposed();
        if (ke2?.Length != OpaqueConstants.KE2_LENGTH)
            throw new ArgumentException($"KE2 must be {OpaqueConstants.KE2_LENGTH} bytes");

        byte[] ke3 = new byte[OpaqueConstants.KE3_LENGTH];

        int result = OpaqueNative.opaque_client_generate_ke3(
            _clientHandle, ke2, (UIntPtr)ke2.Length, keyExchangeState.StateHandle, ke3, (UIntPtr)ke3.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to generate KE3: {(OpaqueResult)result}");

        return ke3;
    }

    /// <summary>
    /// Derives session key after successful authentication
    /// </summary>
    /// <param name="keyExchangeState">State from GenerateKE1</param>
    /// <returns>32-byte session key</returns>
    public byte[] DeriveSessionKey(KeyExchangeResult keyExchangeState)
    {
        ThrowIfDisposed();

        byte[] sessionKey = new byte[OpaqueConstants.SESSION_KEY_LENGTH];

        int result = OpaqueNative.opaque_client_finish(
            _clientHandle, keyExchangeState.StateHandle, sessionKey, (UIntPtr)sessionKey.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to derive session key: {(OpaqueResult)result}");

        return sessionKey;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(OpaqueClient));
    }

    private static void ClearPassword(byte[] password)
    {
        if (password != null)
        {
            Array.Clear(password, 0, password.Length);
        }
    }

    public void Dispose()
    {
        if (!_disposed && _clientHandle != IntPtr.Zero)
        {
            OpaqueNative.opaque_client_destroy(_clientHandle);
            _disposed = true;
        }
    }
}