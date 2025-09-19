using System.Runtime.InteropServices;
using System.Reflection;

namespace Ecliptix.Security.SSL.Native.Native;

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

    [DllImport(LibraryName, EntryPoint = "ecliptix_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult Initialize();

    [DllImport(LibraryName, EntryPoint = "ecliptix_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(LibraryName, EntryPoint = "ecliptix_get_error_message", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetErrorMessage();


    [DllImport(LibraryName, EntryPoint = "ecliptix_verify_ed25519", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult VerifyDigitalSignature(byte* message, nuint messageSize, byte* signature);

    [DllImport(LibraryName, EntryPoint = "ecliptix_encrypt_rsa", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult EncryptRSA(byte* plaintext, nuint plaintextSize,
        byte* ciphertext, nuint* ciphertextSize);

    [DllImport(LibraryName, EntryPoint = "ecliptix_decrypt_rsa", CallingConvention = CallingConvention.Cdecl)]
    public static extern EcliptixResult DecryptRSA(byte* ciphertext, nuint ciphertextSize,
        byte* privateKeyPem, nuint privateKeySize, byte* plaintext, nuint* plaintextSize);
}