using System;
using System.Runtime.InteropServices;

namespace Ecliptix.Opaque.Protocol.Native;

/// <summary>
/// Native library interface for OPAQUE protocol client operations
/// </summary>
internal static class OpaqueNative
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
            string platformLibrary = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "opaque_client.dll" :
                                   RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libopaque_client.dylib" :
                                   "libopaque_client.so";

            if (NativeLibrary.TryLoad(platformLibrary, assembly, searchPath, out IntPtr handle))
                return handle;
        }
        return IntPtr.Zero;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_create(byte[] serverPublicKey, UIntPtr keyLength, out IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void opaque_client_destroy(IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int opaque_client_state_create(out IntPtr handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void opaque_client_state_destroy(IntPtr handle);

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
}