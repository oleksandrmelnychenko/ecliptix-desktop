using System.Runtime.InteropServices;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using Ecliptix.Security.Certificate.Pinning.Constants;

namespace Ecliptix.Security.Certificate.Pinning.Native;

internal static unsafe class CertificatePinningNativeLibrary
{
    private const string LibraryName = CertificatePinningConstants.LibraryNames.SslPinning;

    static CertificatePinningNativeLibrary()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ImportResolver);
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file", Justification = "Native library loading requires file system access")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with RequiresUnreferencedCodeAttribute require dynamic access otherwise can break when trimming application code", Justification = "Assembly.GetExecutingAssembly is AOT-safe")]
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

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult Initialize();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_verify", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult VerifySignature(
        byte* data, nuint dataLen,
        byte* signature, nuint signatureLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_encrypt", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult Encrypt(
        byte* plaintext, nuint plaintextLen,
        byte* ciphertext, nuint* ciphertextLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_decrypt", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult Decrypt(
        byte* ciphertext, nuint ciphertextLen,
        byte* plaintext, nuint* plaintextLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_public_key", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult GetPublicKey(
        byte* publicKeyDer, nuint* publicKeyLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetErrorMessage();
}