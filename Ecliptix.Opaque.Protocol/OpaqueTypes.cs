using System;

namespace Ecliptix.Opaque.Protocol;

public static class OpaqueConstants
{
    public const int OPRF_SEED_LENGTH = 32;
    public const int PRIVATE_KEY_LENGTH = 32;
    public const int PUBLIC_KEY_LENGTH = 32;
    public const int NONCE_LENGTH = 32;
    public const int MAC_LENGTH = 64;
    public const int HASH_LENGTH = 64;
    public const int ENVELOPE_LENGTH = 144;
    public const int REGISTRATION_REQUEST_LENGTH = 32;
    public const int REGISTRATION_RESPONSE_LENGTH = 96;
    public const int REGISTRATION_RECORD_LENGTH = 176;
    public const int CREDENTIAL_REQUEST_LENGTH = 96;
    public const int CREDENTIAL_RESPONSE_LENGTH = 176;
    public const int KE1_LENGTH = 96;
    public const int KE2_LENGTH = 304;
    public const int KE3_LENGTH = 64;
    public const int SESSION_KEY_LENGTH = 64;
}

public enum OpaqueResult : int
{
    Success = 0,
    InvalidInput = -1,
    CryptoError = -2,
    MemoryError = -3,
    ValidationError = -4,
    AuthenticationError = -5,
    InvalidPublicKey = -6
}

public sealed class RegistrationResult : IDisposable
{
    private readonly byte[] _request;
    private readonly IntPtr _stateHandle;
    private bool _disposed;

    public byte[] GetRequestCopy() => (byte[])_request.Clone();

    internal IntPtr StateHandle => _stateHandle;

    internal RegistrationResult(byte[] request, IntPtr stateHandle)
    {
        _request = request;
        _stateHandle = stateHandle;
    }

    public void Dispose()
    {
        if (!_disposed && StateHandle != IntPtr.Zero)
        {
            NativeLibraries.OpaqueNative.opaque_client_state_destroy(StateHandle);
            _disposed = true;
        }
    }
}

public sealed class KeyExchangeResult : IDisposable
{
    private readonly byte[] _keyExchangeData;
    private readonly IntPtr _stateHandle;
    private bool _disposed;

    public byte[] GetKeyExchangeDataCopy() => (byte[])_keyExchangeData.Clone();

    internal IntPtr StateHandle => _stateHandle;

    internal KeyExchangeResult(byte[] keyExchangeData, IntPtr stateHandle)
    {
        _keyExchangeData = keyExchangeData;
        _stateHandle = stateHandle;
    }

    public void Dispose()
    {
        if (!_disposed && StateHandle != IntPtr.Zero)
        {
            NativeLibraries.OpaqueNative.opaque_client_state_destroy(StateHandle);
            _disposed = true;
        }
    }
}

public static class OpaqueErrorMessages
{
    public const string ServerPublicKeyInvalidSize = "Server public key must be exactly {0} bytes";
    public const string FailedToCreateOpaqueClient = "Failed to create OPAQUE client: {0}";
    public const string PasswordNullOrEmpty = "Password cannot be null or empty";
    public const string FailedToCreateState = "Failed to create state: {0}";
    public const string FailedToCreateRegistrationRequest = "Failed to create registration request: {0}";
    public const string ServerResponseInvalidSize = "Server response must be exactly {0} bytes";
    public const string FailedToFinalizeRegistration = "Failed to finalize registration: {0}";
    public const string FailedToGenerateKE1 = "Failed to generate KE1: {0}";
    public const string FailedToDeriveSessionKey = "Failed to derive session key: {0}";
}
