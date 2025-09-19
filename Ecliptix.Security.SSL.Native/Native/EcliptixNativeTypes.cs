using System.Runtime.InteropServices;

namespace Ecliptix.Security.SSL.Native.Native;

/// <summary>
/// Result codes from the native Ecliptix library
/// </summary>
public enum EcliptixResult : int
{
    Success = 0,
    ErrorInvalidArgument = -1,
    ErrorMemoryAllocation = -2,
    ErrorInitialization = -3,
    ErrorCryptography = -4,
    ErrorCertificateInvalid = -5,
    ErrorCertificateExpired = -6,
    ErrorPinMismatch = -7,
    ErrorHostnameMismatch = -8,
    ErrorEncryptionFailed = -9,
    ErrorDecryptionFailed = -10,
    ErrorSignatureFailed = -11,
    ErrorVerificationFailed = -12,
    ErrorNotInitialized = -13,
    ErrorInternalFailure = -14
}

/// <summary>
/// Initialization options for the Ecliptix library
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixInitOptions
{
    public uint Version;
    public uint Flags;
    public byte* LogLevel;
    public delegate* unmanaged[Cdecl]<EcliptixErrorInfo*, void*, void> ErrorCallback;
    public void* ErrorCallbackUserData;
}

/// <summary>
/// Error information structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixErrorInfo
{
    public EcliptixResult ErrorCode;
    public byte* ErrorMessage;
    public uint ErrorMessageLength;
    public byte* SourceFile;
    public uint SourceLine;
    public ulong Timestamp;
}

/// <summary>
/// Certificate information structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixCertificateInfo
{
    public byte* Subject;
    public uint SubjectLength;
    public byte* Issuer;
    public uint IssuerLength;
    public byte* SerialNumber;
    public uint SerialNumberLength;
    public ulong NotBefore;
    public ulong NotAfter;
    public byte* Fingerprint;
    public uint FingerprintLength;
}

/// <summary>
/// AES-GCM encryption result
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixEncryptResult
{
    public byte* Ciphertext;
    public uint CiphertextLength;
    public byte* Iv;
    public uint IvLength;
    public byte* Tag;
    public uint TagLength;
}

/// <summary>
/// Memory buffer for secure operations
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixSecureBuffer
{
    public byte* Data;
    public nuint Length;
    public nuint Capacity;
}

/// <summary>
/// Version information structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixVersionInfo
{
    public uint MajorVersion;
    public uint MinorVersion;
    public uint PatchVersion;
    public byte* BuildDate;
    public byte* GitCommit;
    public uint BuildTimestamp;
}

/// <summary>
/// Performance statistics structure
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EcliptixStats
{
    public ulong TotalOperations;
    public ulong SuccessfulOperations;
    public ulong FailedOperations;
    public ulong EncryptionOperations;
    public ulong DecryptionOperations;
    public ulong SigningOperations;
    public ulong VerificationOperations;
    public ulong HashingOperations;
    public ulong CertificateValidations;
    public double AverageOperationTimeMs;
    public ulong BytesEncrypted;
    public ulong BytesDecrypted;
    public ulong MemoryAllocated;
    public ulong MemoryFreed;
}

/// <summary>
/// Constants used by the native library
/// </summary>
public static class EcliptixConstants
{
    public const int MaxHostnameLength = 253;
    public const int MaxErrorMessageLength = 512;
    public const int AesGcmKeySize = 32;
    public const int AesGcmIvSize = 12;
    public const int AesGcmTagSize = 16;
    public const int Ed25519PublicKeySize = 32;
    public const int Ed25519PrivateKeySize = 32;
    public const int Ed25519SignatureSize = 64;
    public const int Sha384HashSize = 48;
    public const int Blake2bDefaultSize = 32;
    public const int Blake2bMaxSize = 64;
    public const int Blake2bMinSize = 16;
    public const int ChaCha20KeySize = 32;
    public const int ChaCha20NonceSize = 12;
    public const int ChaCha20TagSize = 16;
    public const uint CurrentVersion = 1;

    // AEAD Algorithm identifiers
    public const uint AlgorithmAesGcm = 1;
    public const uint AlgorithmChaCha20Poly1305 = 2;
    public const uint AlgorithmXChaCha20Poly1305 = 3;
}