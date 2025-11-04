namespace Ecliptix.Utilities;

internal static class UtilityConstants
{
    internal static class Hash
    {
        public const int INITIAL_HASH_SEED = 17;
        public const int HASH_MULTIPLIER = 31;
    }

    internal static class ErrorMessages
    {
        public const string CANNOT_UNWRAP_ERR = "Cannot unwrap an Err result";
        public const string CANNOT_UNWRAP_OK = "Cannot unwrap an Ok result";
        public const string ERROR_MAPPER_RETURNED_NULL = "ERROR mapper returned null, violating TE : notnull";
        public const string INSUFFICIENT_ENTROPY = "Random number generator appears to have insufficient entropy";
        public const string INVALID_APP_INSTANCE_ID_FORMAT = "Invalid AppInstanceId format: ";
        public const string INVALID_APP_DEVICE_ID_FORMAT = "Invalid APP_DEVICE_ID format: ";
    }

    internal static class Cryptography
    {
        public const int MAX_ENTROPY_CHECK_ATTEMPTS = 10;
        public const int GUID_SIZE_BYTES = 16;
        public const int U_INT_32_SIZE_BYTES = 4;
        public const int STACK_ALLOC_THRESHOLD = 512;
        public const int SHA_256_OUTPUT_SIZE = 32;
        public const byte MAX_BYTE_VALUE = 255;
        public const byte MIN_BYTE_VALUE = 0;
        public const int HASH_BYTES_TO_READ = 4;
    }

    internal static class UnitType
    {
        public const int HASH_CODE = 0;
        public const string STRING_REPRESENTATION = "()";
    }

    internal static class ResultType
    {
        public const string OK_STRING = "Ok";
        public const string ERR_STRING = "Err";
    }

    internal static class ProtocolNames
    {
        public const string X_3DH_INFO = "Ecliptix_X3DH";
    }

    internal static class ProtocolBytes
    {
        public const byte MSG_INFO_VALUE = 0x01;
        public const byte CHAIN_INFO_VALUE = 0x02;
    }

    internal static class NetworkConstants
    {
        public const uint MIN_REQUEST_ID = 10;
    }
}
