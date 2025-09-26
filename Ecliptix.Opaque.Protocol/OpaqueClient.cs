using System;
using Ecliptix.Opaque.Protocol.NativeLibraries;

namespace Ecliptix.Opaque.Protocol;

public sealed class OpaqueClient : IDisposable
{
    private readonly IntPtr _clientHandle;
    private bool _disposed;

    public OpaqueClient(byte[] serverPublicKey)
    {
        if (serverPublicKey?.Length != OpaqueConstants.PUBLIC_KEY_LENGTH)
            throw new ArgumentException($"Server public key must be {OpaqueConstants.PUBLIC_KEY_LENGTH} bytes");

        int result =
            OpaqueNative.opaque_client_create(serverPublicKey, (UIntPtr)serverPublicKey.Length, out _clientHandle);
        if (result != (int)OpaqueResult.Success || _clientHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create OPAQUE client: {(OpaqueResult)result}");
    }

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

    public byte[] FinalizeRegistration(byte[] serverResponse, RegistrationResult registrationState)
    {
        ThrowIfDisposed();
        if (serverResponse?.Length != OpaqueConstants.REGISTRATION_RESPONSE_LENGTH)
            throw new ArgumentException(
                $"Server response must be {OpaqueConstants.REGISTRATION_RESPONSE_LENGTH} bytes");

        byte[] record = new byte[OpaqueConstants.REGISTRATION_RECORD_LENGTH];

        int result = OpaqueNative.opaque_client_finalize_registration(
            _clientHandle, serverResponse, (UIntPtr)serverResponse.Length,
            registrationState.StateHandle, record, (UIntPtr)record.Length);

        if (result != (int)OpaqueResult.Success)
            throw new InvalidOperationException($"Failed to finalize registration: {(OpaqueResult)result}");

        return record;
    }

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

    public byte[] DeriveSessionKey(KeyExchangeResult keyExchangeState)
    {
        ThrowIfDisposed();

        byte[] sessionKey = new byte[OpaqueConstants.HASH_LENGTH];

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
        Array.Clear(password, 0, password.Length);
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