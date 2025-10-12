namespace Ecliptix.Utilities;

public static class UtilityConstants
{
    public static class Hash
    {
        public const int InitialHashSeed = 17;
        public const int HashMultiplier = 31;
    }

    public static class ErrorMessages
    {
        public const string CannotUnwrapErr = "Cannot unwrap an Err result";
        public const string CannotUnwrapOk = "Cannot unwrap an Ok result";
        public const string ErrorMapperReturnedNull = "Error mapper returned null, violating TE : notnull";
        public const string InsufficientEntropy = "Random number generator appears to have insufficient entropy";
        public const string InvalidAppInstanceIdFormat = "Invalid AppInstanceId format: ";
        public const string InvalidAppDeviceIdFormat = "Invalid AppDeviceId format: ";
    }

    public static class Cryptography
    {
        public const int MaxEntropyCheckAttempts = 10;
        public const int GuidSizeBytes = 16;
        public const int UInt32SizeBytes = 4;
        public const int StackAllocThreshold = 512;
        public const int Sha256OutputSize = 32;
        public const byte MaxByteValue = 255;
        public const byte MinByteValue = 0;
        public const int HashBytesToRead = 4;
    }

    public static class UnitType
    {
        public const int HashCode = 0;
        public const string StringRepresentation = "()";
    }

    public static class ResultType
    {
        public const string OkString = "Ok";
        public const string ErrString = "Err";
    }

    public static class ProtocolNames
    {
        public const string X3dhInfo = "Ecliptix_X3DH";
    }

    public static class ProtocolBytes
    {
        public const byte MsgInfoValue = 0x01;
        public const byte ChainInfoValue = 0x02;
    }

    public static class NetworkConstants
    {
        public const uint MinRequestId = 10;
    }
}