using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ecliptix.Opaque.Protocol.NativeLibraries;

internal static partial class OpaqueNative
{
    private const string Library = "libopaque_client";

    static OpaqueNative()
    {
        NativeLibrary.SetDllImportResolver(typeof(OpaqueNative).Assembly, DllImportResolver);
    }

    private static IntPtr DllImportResolver(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName == Library)
        {
            string platformLibrary = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "libopaque_client.dll" :
                                   RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libopaque_client.dylib" :
                                   "libopaque_client.so";

            if (NativeLibrary.TryLoad(platformLibrary, assembly, searchPath, out IntPtr handle))
            {
                return handle;
            }
        }
        return IntPtr.Zero;
    }

    [LibraryImport(Library, EntryPoint = "opaque_client_create")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int opaque_client_create(byte[] serverPublicKey, nuint keyLength, out IntPtr handle);

    [LibraryImport(Library, EntryPoint = "opaque_client_destroy")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void opaque_client_destroy(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "opaque_client_state_create")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int opaque_client_state_create(out IntPtr handle);

    [LibraryImport(Library, EntryPoint = "opaque_client_state_destroy")]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void opaque_client_state_destroy(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_create_registration_request(
        IntPtr clientHandle, byte[] password, UIntPtr passwordLength, IntPtr stateHandle, byte[] requestData, UIntPtr requestBufferSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_finalize_registration(
        IntPtr clientHandle, byte[] responseData, UIntPtr responseLength, IntPtr stateHandle, byte[] recordData, UIntPtr recordBufferSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_generate_ke1(
        IntPtr clientHandle, byte[] password, UIntPtr passwordLength, IntPtr stateHandle, byte[] ke1Data, UIntPtr ke1BufferSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_generate_ke3(
        IntPtr clientHandle, byte[] ke2Data, UIntPtr ke2Length, IntPtr stateHandle, byte[] ke3Data, UIntPtr ke3BufferSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_finish(
        IntPtr clientHandle, IntPtr stateHandle, byte[] sessionKey, UIntPtr sessionKeyBufferSize);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_create_default(out IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr opaque_client_get_version();
}
