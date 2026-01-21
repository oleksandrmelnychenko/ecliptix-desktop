namespace Ecliptix.Protected.Protocol;

internal static class ProtocolSystemConstants
{
    public static class ErrorMessages
    {
        public const string HANDLE_DISPOSED = "Handle disposed";
        public const string DATA_EXCEEDS_BUFFER = "Data ({0}) > buffer ({1})";
        public const string REF_COUNT_FAILED = "Ref count failed";
        public const string UNEXPECTED_WRITE_ERROR = "Unexpected write error";
        public const string SECURE_STRING_HANDLER_DISPOSED = "SecureStringHandler is disposed";
    }

    public static class Numeric
    {
        public const int DLL_IMPORT_SUCCESS = 0;
    }

    public static class Libraries
    {
        public const string LIB_SODIUM = "libsodium";
        public const string KERNEL_32 = "kernel32.dll";
        public const string LIB_C = "libc";
    }
}
