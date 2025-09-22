/*
 * Ecliptix Security SSL Native Library
 * P/Invoke wrapper for the new Ecliptix client security library
 * Author: Oleksandr Melnychenko
 */

using System.Runtime.InteropServices;
using System.Reflection;

namespace Ecliptix.Security.SSL.Native.Native;

internal static unsafe class EcliptixNativeLibrary
{
    private const string LibraryName = "libecliptix.client";

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

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult Initialize();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_error_message", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetErrorMessage();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_library_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GetLibraryVersion(byte* versionBuffer, nuint bufferSize);

    #endregion

    #region Certificate Validation and SSL Pinning

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_validate_certificate", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult ValidateCertificate(
        byte* certDer, nuint certSize, byte* hostname);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_verify_certificate_pin", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult VerifyCertificatePin(
        byte* certDer, nuint certSize,
        byte* hostname, EcliptixPin* expectedPin);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_certificate_pin", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GetCertificatePin(
        byte* certDer, nuint certSize, EcliptixPin* pinOut);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_validate_pin_config", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult ValidatePinConfig(
        EcliptixPinConfig* pinConfig);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_is_hostname_trusted", CallingConvention = CallingConvention.Cdecl)]
    public static extern int IsHostnameTrusted(byte* hostname);

    #endregion

    #region Signature Verification

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_verify_ed25519_signature", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult VerifyEd25519Signature(
        byte* message, nuint messageSize,
        byte* signature, byte* publicKey);

    #endregion

    #region RSA Encryption

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_encrypt_rsa", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult EncryptRSA(
        byte* plaintext, nuint plaintextSize,
        byte* ciphertext, nuint* ciphertextSize);

    #endregion

    #region Cryptographic Utilities

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_hash_sha256", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult HashSha256(
        byte* data, nuint dataSize, byte* hashOut);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_hash_sha384", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult HashSha384(
        byte* data, nuint dataSize, byte* hashOut);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_generate_random", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GenerateRandom(
        byte* buffer, nuint bufferSize);

    #endregion
}