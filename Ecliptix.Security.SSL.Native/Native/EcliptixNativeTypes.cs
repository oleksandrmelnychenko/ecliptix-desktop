using System.Runtime.InteropServices;

namespace Ecliptix.Security.SSL.Native.Native;

public enum EcliptixResult
{
    Success = 0,
    ErrorNotInitialized = -1,
    ErrorSessionNotFound = -2,
    ErrorInvalidParam = -3,
    ErrorMemoryAllocation = -4,
    ErrorCryptoFailure = -5,
    ErrorKeyLoadFailed = -6,
    ErrorCertificateInvalid = -7,
    ErrorSignatureInvalid = -8,
    ErrorBufferTooSmall = -9,
    ErrorPinVerificationFailed = -10,
    ErrorSignatureFailed = -11,
    ErrorDecryptionFailed = -12,
    ErrorEncryptionFailed = -13,
    ErrorUnknown = -99
}

public static class EcliptixConstants
{
    // Key and signature sizes
    public const int Ed25519PublicKeySize = 32;
    public const int Ed25519SignatureSize = 64;

    // RSA configuration
    public const int RsaMaxPlaintextSize = 214;
    public const int RsaCiphertextSize = 256;

    // Hash sizes
    public const int Sha256HashSize = 32;
    public const int Sha384HashSize = 48;

    // Certificate validation
    public const int MaxHostnameSize = 256;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixPin
{
    public fixed byte Hash[EcliptixConstants.Sha384HashSize];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public unsafe struct EcliptixPinConfig
{
    public fixed byte Hostname[EcliptixConstants.MaxHostnameSize];
    public EcliptixPin PrimaryPin;
    public fixed byte BackupPins[3 * EcliptixConstants.Sha384HashSize];
    public nuint BackupPinCount;
}