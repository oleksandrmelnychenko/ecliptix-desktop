using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Ecliptix.Security.Certificate.Pinning.Constants;

namespace Ecliptix.Security.Certificate.Pinning.Native;

internal static unsafe class CertificatePinningNativeLibrary
{
    private const string LIBRARY_NAME = CertificatePinningConstants.LibraryNames.SSL_PINNING;

    static CertificatePinningNativeLibrary()
    {
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ImportResolver);
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file", Justification = "Native library loading requires file system access")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with RequiresUnreferencedCodeAttribute require dynamic access otherwise can break when trimming application code", Justification = "Assembly.GetExecutingAssembly is AOT-safe")]
    private static IntPtr ImportResolver(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != LIBRARY_NAME)
        {
            return IntPtr.Zero;
        }

        string extension;
        string fileName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileName = "ecliptix.client.dll";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            extension = ".dylib";
            fileName = $"{LIBRARY_NAME}{extension}";
        }
        else
        {
            extension = ".so";
            fileName = $"{LIBRARY_NAME}{extension}";
        }

        string[] searchPaths =
        [
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "runtimes", GetRuntimeIdentifier(), "native", fileName),
            Path.Combine(Path.GetDirectoryName(assembly.Location) ?? string.Empty, fileName)
        ];

        foreach (string libPath in searchPaths)
        {
            if (!File.Exists(libPath))
            {
                continue;
            }

            try
            {
                return NativeLibrary.Load(libPath);
            }
            catch (Exception)
            {
                // Try next library path if this one fails to load
            }
        }

        return IntPtr.Zero;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "win-x86" : "win-x64";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
        }
        return "linux-x64";
    }

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_init", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult Initialize();

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_cleanup", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Cleanup();

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_verify", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult VerifySignature(
        byte* data, nuint dataLen,
        byte* signature, nuint signatureLen);

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_encrypt", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult Encrypt(
        byte* plaintext, nuint plaintextLen,
        byte* ciphertext, nuint* ciphertextLen);

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_decrypt", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult Decrypt(
        byte* ciphertext, nuint ciphertextLen,
        byte* plaintext, nuint* plaintextLen);

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_get_public_key", CallingConvention = CallingConvention.Cdecl)]
    public static extern CertificatePinningNativeResult GetPublicKey(
        byte* publicKeyDer, nuint* publicKeyLen);

    [DllImport(LIBRARY_NAME, EntryPoint = "ecliptix_client_get_error", CallingConvention = CallingConvention.Cdecl)]
    public static extern byte* GetErrorMessage();
}
