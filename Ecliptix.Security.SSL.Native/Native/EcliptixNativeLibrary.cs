using System.Runtime.InteropServices;
using System.Reflection;

namespace Ecliptix.Security.SSL.Native.Native;

/// <summary>
/// P/Invoke wrapper for the native Ecliptix SSL pinning library
/// </summary>
internal static unsafe class EcliptixNativeLibrary
{
    private const string LibraryName = "libecliptix_security";

    static EcliptixNativeLibrary()
    {
        NativeLibrary.SetDllImportResolver(typeof(EcliptixNativeLibrary).Assembly, ImportResolver);
    }

    private static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == LibraryName)
        {
            string extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" :
                              RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ".dylib" : ".so";

            string libPath = Path.Combine(AppContext.BaseDirectory, $"{LibraryName}{extension}");

            if (File.Exists(libPath))
            {
                return NativeLibrary.Load(libPath);
            }
        }

        return IntPtr.Zero;
    }

    #region Library Management

    [DllImport(LibraryName, EntryPoint = "ecliptix_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult Initialize();

    [DllImport(LibraryName, EntryPoint = "ecliptix_init_ex", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult InitializeEx(delegate* unmanaged[Cdecl]<int, byte*, void> logCallback,
        delegate* unmanaged[Cdecl]<EcliptixErrorInfo*, void*, void> errorCallback, void* userData);

    [DllImport(LibraryName, EntryPoint = "ecliptix_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(LibraryName, EntryPoint = "ecliptix_is_initialized", CallingConvention = CallingConvention.Cdecl)]
    public static extern int IsInitialized();

    [DllImport(LibraryName, EntryPoint = "ecliptix_get_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GetVersion(EcliptixVersionInfo* versionInfo);

    [DllImport(LibraryName, EntryPoint = "ecliptix_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetVersionString();

    #endregion

    #region SSL Certificate Validation

    [DllImport(LibraryName, EntryPoint = "ecliptix_validate_certificate", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult ValidateCertificate(byte* certData, nuint certSize, byte* hostname, uint validationFlags);

    [DllImport(LibraryName, EntryPoint = "ecliptix_check_certificate_pin", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult CheckCertificatePin(byte* certData, nuint certSize, uint pinMode);

    #endregion

    #region Encryption/Decryption

    [DllImport(LibraryName, EntryPoint = "ecliptix_generate_random", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GenerateRandom(byte* buffer, nuint size);

    [DllImport(LibraryName, EntryPoint = "ecliptix_encrypt_aead", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult EncryptAead(byte* plaintext, nuint plaintextSize,
        byte* key, nuint keySize, byte* associatedData, nuint associatedDataSize,
        byte* nonce, nuint nonceSize, uint algorithm,
        byte* ciphertext, nuint* ciphertextSize, byte* tag, nuint* tagSize);

    [DllImport(LibraryName, EntryPoint = "ecliptix_decrypt_aead", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult DecryptAead(byte* ciphertext, nuint ciphertextSize,
        byte* tag, nuint tagSize, byte* key, nuint keySize,
        byte* associatedData, nuint associatedDataSize, byte* nonce, nuint nonceSize,
        uint algorithm, byte* plaintext, nuint* plaintextSize);

    #endregion

    #region Digital Signatures

    [DllImport(LibraryName, EntryPoint = "ecliptix_sign_ed25519", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult SignEd25519(byte* message, nuint messageSize, byte* signature);

    [DllImport(LibraryName, EntryPoint = "ecliptix_verify_ed25519", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult VerifyEd25519(byte* message, nuint messageSize, byte* signature);

    #endregion

    #region RSA Encryption/Decryption

    [DllImport(LibraryName, EntryPoint = "ecliptix_encrypt_rsa", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult EncryptRsa(byte* plaintext, nuint plaintextSize,
        byte* ciphertext, nuint* ciphertextSize);

    [DllImport(LibraryName, EntryPoint = "ecliptix_decrypt_rsa", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult DecryptRsa(byte* ciphertext, nuint ciphertextSize,
        byte* privateKeyPem, nuint privateKeySize, byte* plaintext, nuint* plaintextSize);

    #endregion

    #region Hashing

    [DllImport(LibraryName, EntryPoint = "ecliptix_hash_blake2b", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult HashBlake2b(byte* data, nuint dataSize,
        byte* hash, nuint* hashSize, byte* key, nuint keySize);

    #endregion

    #region Statistics

    [DllImport(LibraryName, EntryPoint = "ecliptix_get_stats", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GetStats(EcliptixStats* stats);

    #endregion

    #region Memory Management

    [DllImport(LibraryName, EntryPoint = "ecliptix_secure_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void SecureFree(void* ptr, nuint size);

    [DllImport(LibraryName, EntryPoint = "ecliptix_get_error_message", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetErrorMessage();

    #endregion
}