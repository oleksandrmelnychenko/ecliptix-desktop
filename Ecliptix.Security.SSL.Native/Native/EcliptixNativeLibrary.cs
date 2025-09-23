using System.Runtime.InteropServices;
using System.Reflection;

namespace Ecliptix.Security.SSL.Native.Native;

internal static unsafe class EcliptixNativeLibrary
{
    private const string LibraryName = "libsslpinning";

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

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult Initialize();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_verify", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult VerifySignature(
        byte* data, nuint dataLen,
        byte* signature, nuint signatureLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_encrypt", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult Encrypt(
        byte* plaintext, nuint plaintextLen,
        byte* ciphertext, nuint* ciphertextLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_decrypt", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult Decrypt(
        byte* ciphertext, nuint ciphertextLen,
        byte* plaintext, nuint* plaintextLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_public_key", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult GetPublicKey(
        byte* publicKeyDer, nuint* publicKeyLen);

    [DllImport(LibraryName, EntryPoint = "ecliptix_client_get_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetErrorMessage();
}