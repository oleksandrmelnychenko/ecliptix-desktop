using System;
using System.Security.Cryptography;
using Ecliptix.Opaque.Protocol.NativeLibraries;
using Ecliptix.Utilities;

namespace Ecliptix.Opaque.Protocol;

public sealed class OpaqueClient : IDisposable
{
    private readonly IntPtr _clientHandle;
    private bool _disposed;

    public OpaqueClient(byte[] serverPublicKey)
    {
        if (serverPublicKey?.Length != OpaqueConstants.PUBLIC_KEY_LENGTH)
        {
            throw new ArgumentException(string.Format(OpaqueErrorMessages.ServerPublicKeyInvalidSize, OpaqueConstants.PUBLIC_KEY_LENGTH));
        }

        int result =
            OpaqueNative.opaque_client_create(serverPublicKey, (UIntPtr)serverPublicKey.Length, out _clientHandle);
        if (result != (int)OpaqueResult.Success || _clientHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToCreateOpaqueClient, (OpaqueResult)result));
        }
    }

    public RegistrationResult CreateRegistrationRequest(byte[] secureKey)
    {
        ThrowIfDisposed();
        if (secureKey == null || secureKey.Length == 0)
        {
            throw new ArgumentException(OpaqueErrorMessages.SecureKeyNullOrEmpty);
        }

        try
        {
            byte[] request = new byte[OpaqueConstants.REGISTRATION_REQUEST_LENGTH];

            int stateResult = OpaqueNative.opaque_client_state_create(out IntPtr state);
            if (stateResult != (int)OpaqueResult.Success)
            {
                throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToCreateState, (OpaqueResult)stateResult));
            }

            int result = OpaqueNative.opaque_client_create_registration_request(
                _clientHandle, secureKey, (UIntPtr)secureKey.Length, state, request, (UIntPtr)request.Length);

            if (result != (int)OpaqueResult.Success)
            {
                OpaqueNative.opaque_client_state_destroy(state);
                throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToCreateRegistrationRequest, (OpaqueResult)result));
            }

            return new RegistrationResult(request, state);
        }
        finally
        {
            ClearSecureKey(secureKey);
        }
    }

    public (byte[] Record, byte[] MasterKey) FinalizeRegistration(byte[] serverResponse, RegistrationResult registrationState)
    {
        ThrowIfDisposed();
        if (serverResponse?.Length != OpaqueConstants.REGISTRATION_RESPONSE_LENGTH)
        {
            throw new ArgumentException(
                string.Format(OpaqueErrorMessages.ServerResponseInvalidSize, OpaqueConstants.REGISTRATION_RESPONSE_LENGTH));
        }

        byte[] masterKey = new byte[OpaqueConstants.MASTER_KEY_LENGTH];
        System.Security.Cryptography.RandomNumberGenerator.Fill(masterKey);

        byte[] record = new byte[OpaqueConstants.REGISTRATION_RECORD_LENGTH];

        int result = OpaqueNative.opaque_client_finalize_registration(
            _clientHandle, serverResponse, (UIntPtr)serverResponse.Length,
            masterKey, (UIntPtr)masterKey.Length,
            registrationState.StateHandle, record, (UIntPtr)record.Length);

        if (result != (int)OpaqueResult.Success)
        {
            throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToFinalizeRegistration, (OpaqueResult)result));
        }

        return (record, masterKey);
    }

    public KeyExchangeResult GenerateKE1(byte[] secureKey)
    {
        ThrowIfDisposed();
        if (secureKey == null || secureKey.Length == 0)
        {
            throw new ArgumentException(OpaqueErrorMessages.SecureKeyNullOrEmpty);
        }

        try
        {
            byte[] ke1 = new byte[OpaqueConstants.KE1_LENGTH];

            int stateResult = OpaqueNative.opaque_client_state_create(out IntPtr state);
            if (stateResult != (int)OpaqueResult.Success)
            {
                throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToCreateState, (OpaqueResult)stateResult));
            }

            int result = OpaqueNative.opaque_client_generate_ke1(
                _clientHandle, secureKey, (UIntPtr)secureKey.Length, state, ke1, (UIntPtr)ke1.Length);

            if (result != (int)OpaqueResult.Success)
            {
                OpaqueNative.opaque_client_state_destroy(state);
                throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToGenerateKE1, (OpaqueResult)result));
            }

            return new KeyExchangeResult(ke1, state);
        }
        finally
        {
            ClearSecureKey(secureKey);
        }
    }

    public Result<byte[], OpaqueResult> GenerateKe3(byte[]? ke2, KeyExchangeResult keyExchangeState)
    {
        ThrowIfDisposed();
        if (ke2?.Length != OpaqueConstants.KE2_LENGTH)
        {
            return Result<byte[], OpaqueResult>.Err(OpaqueResult.InvalidInput);
        }

        byte[] ke3 = new byte[OpaqueConstants.KE3_LENGTH];

        int result = OpaqueNative.opaque_client_generate_ke3(
            _clientHandle, ke2, (UIntPtr)ke2.Length, keyExchangeState.StateHandle, ke3, (UIntPtr)ke3.Length);

        return result != (int)OpaqueResult.Success
            ? Result<byte[], OpaqueResult>.Err((OpaqueResult)result)
            : Result<byte[], OpaqueResult>.Ok(ke3);
    }

    public (byte[] SessionKey, byte[] MasterKey) DeriveBaseMasterKey(KeyExchangeResult keyExchangeState)
    {
        ThrowIfDisposed();

        byte[] sessionKey = new byte[OpaqueConstants.HASH_LENGTH];
        byte[] masterKey = new byte[OpaqueConstants.MASTER_KEY_LENGTH];

        int result = OpaqueNative.opaque_client_finish(
            _clientHandle, keyExchangeState.StateHandle,
            sessionKey, (UIntPtr)sessionKey.Length,
            masterKey, (UIntPtr)masterKey.Length);

        if (result != (int)OpaqueResult.Success)
        {
            throw new InvalidOperationException(string.Format(OpaqueErrorMessages.FailedToDeriveSessionKey, (OpaqueResult)result));
        }

        return (sessionKey, masterKey);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(OpaqueClient));
        }
    }

    private static void ClearSecureKey(byte[] secureKey)
    {
        CryptographicOperations.ZeroMemory(secureKey);
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
