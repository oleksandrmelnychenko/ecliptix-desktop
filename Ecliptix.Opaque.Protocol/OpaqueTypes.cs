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
    public byte[] Request { get; }
    public IntPtr StateHandle { get; }
    private bool _disposed;

    internal RegistrationResult(byte[] request, IntPtr stateHandle)
    {
        Request = request;
        StateHandle = stateHandle;
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
    public byte[] KeyExchangeData { get; }
    public IntPtr StateHandle { get; }
    private bool _disposed;

    internal KeyExchangeResult(byte[] keyExchangeData, IntPtr stateHandle)
    {
        KeyExchangeData = keyExchangeData;
        StateHandle = stateHandle;
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
