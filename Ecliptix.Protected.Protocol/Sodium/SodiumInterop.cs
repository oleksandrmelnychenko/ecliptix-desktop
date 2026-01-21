using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protected.Protocol.Sodium;

internal static partial class SodiumInterop
{
    private const string LIB_SODIUM = ProtocolSystemConstants.Libraries.LIB_SODIUM;

    private static readonly Result<Unit, SodiumFailure> InitializationResult = InitializeSodium();

    public static bool IsInitialized => InitializationResult.IsOk;

    [LibraryImport(LIB_SODIUM, EntryPoint = "sodium_init")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int sodium_init();

    [LibraryImport(LIB_SODIUM, EntryPoint = "sodium_malloc")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr sodium_malloc(nuint size);

    [LibraryImport(LIB_SODIUM, EntryPoint = "sodium_free")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void sodium_free(IntPtr ptr);

    private static Result<Unit, SodiumFailure> InitializeSodium()
    {
        return Result<Unit, SodiumFailure>.Try(
            () =>
            {
                int result = sodium_init();
                const int dllImportSuccess = ProtocolSystemConstants.Numeric.DLL_IMPORT_SUCCESS;
                if (result < dllImportSuccess)
                {
                    throw new InvalidOperationException(SodiumFailureMessages.SODIUM_INIT_FAILED);
                }
            },
            ex => ex switch
            {
                DllNotFoundException dllEx => SodiumFailure.LibraryNotFound(
                    string.Format(SodiumFailureMessages.LIBRARY_LOAD_FAILED, LIB_SODIUM), dllEx),
                InvalidOperationException opEx when opEx.Message.Contains(SodiumExceptionMessagePatterns
                        .SODIUM_INIT_PATTERN) =>
                    SodiumFailure.InitializationFailed(SodiumFailureMessages.INITIALIZATION_FAILED, opEx),
                _ => SodiumFailure.InitializationFailed(SodiumFailureMessages.UNEXPECTED_INIT_ERROR, ex)
            }
        );
    }
}
