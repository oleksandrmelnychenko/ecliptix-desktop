namespace Ecliptix.Protocol.System;

public static class ProtocolSystemConstants
{
    public static class Timeouts
    {
        public static readonly TimeSpan SessionTimeout = TimeSpan.FromHours(24);
        public static readonly TimeSpan DefaultCircuitBreakerTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan DefaultMetricsInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan WindowAdjustmentInterval = TimeSpan.FromMinutes(2);
        public static readonly TimeSpan AnalysisInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan MessageWindowSize = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan ConfigUpdateInterval = TimeSpan.FromMinutes(5);

        public static readonly TimeSpan LightLoadMaxChainAge = TimeSpan.FromMinutes(30);
        public static readonly TimeSpan ModerateLoadMaxChainAge = TimeSpan.FromMinutes(45);
        public static readonly TimeSpan HeavyLoadMaxChainAge = TimeSpan.FromHours(1);
        public static readonly TimeSpan ExtremeLoadMaxChainAge = TimeSpan.FromHours(2);
        public static readonly TimeSpan DefaultMaxChainAge = TimeSpan.FromHours(1);
    }

    public static class MemoryPool
    {
        public const int DefaultBufferSize = 4096;
        public const int MaxPoolSize = 100;
        public const int SecureWipeChunkSize = 1024;
        public const int SecureStringBuilderDefaultChunkSize = 256;
    }

    public static class ErrorMessages
    {
        public const string FailedToAllocateSecureMemory = "Failed to allocate secure memory: ";
        public const string RequestedSizeExceedsAllocated = "Requested size {0} exceeds allocated size {1}";
        public const string FailedToReadSecureMemory = "Failed to read secure memory: ";
        public const string BufferDisposed = "Buffer is disposed";
        public const string HandleDisposed = "Handle disposed";
        public const string DataExceedsBuffer = "Data ({0}) > buffer ({1})";
        public const string RefCountFailed = "Ref count failed";
        public const string UnexpectedWriteError = "Unexpected write error";
        public const string BufferSizePositive = "Buffer size must be positive";
        public const string MaxPoolSizePositive = "Max pool size must be positive";
        public const string SizePositive = "Size must be positive";
        public const string ChunkSizePositive = "Chunk size must be positive";
        public const string LibSodiumConstantTimeComparisonFailed = "libsodium constant-time comparison failed.";
    }

    public static class JsonFormatting
    {
        public const string EscapeQuote = "\\\"";
        public const string EscapeBackslash = "\\\\";
        public const string EscapeBackspace = "\\b";
        public const string EscapeFormFeed = "\\f";
        public const string EscapeNewLine = "\\n";
        public const string EscapeCarriageReturn = "\\r";
        public const string EscapeTab = "\\t";
        public const string UnicodeEscapeFormat = "\\u{0:X4}";
        public const string TimestampField = "  \"Timestamp\": \"{0:O}\",";
        public const string SessionDurationField = "  \"SessionDuration\": \"{0}\",";
        public const string OperationsArrayStart = "  \"Operations\": [";
        public const string OperationObjectStart = "    {";
        public const string NameField = "      \"Name\": \"{0}\",";
        public const string ExecutionCountField = "      \"ExecutionCount\": {0},";
        public const string AverageField = "      \"AverageMs\": {0},";
        public const string MaxField = "      \"MaxMs\": {0},";
        public const string MinField = "      \"MinMs\": {0},";
        public const string TotalField = "      \"TotalMs\": {0}";
        public const string OperationObjectEnd = "    }";
        public const string ArrayEnd = "  ]";
    }

    public static class Numeric
    {
        public const int PerformanceDecimalPlaces = 3;
        public const int JsonEscapeBufferExtra = 16;
        public const int WindowMultiplierHeavy = 3;
        public const int WindowMultiplierModerate = 2;
        public const int DllImportSuccess = 0;
        public const int ZeroValue = 0;
        public const int NegativeOneValue = -1;
    }

    public static class Protocol
    {
        public const string InitialSenderChainInfo = "ShieldInitSend";
        public const string InitialReceiverChainInfo = "ShieldInitRecv";
        public const string DhRatchetInfo = "ShieldDhRatchet";

        public const long InitialNonceCounter = 0;
        public const long MaxNonceCounter = 10_000_000;
        public const int RandomNoncePrefixSize = 8;
        public const uint DefaultChainIndex = 0;
        public const uint DefaultMessageIndex = 0;
        public const int HkdfOutputBufferMultiplier = 2;
    }

    public static class RatchetRecovery
    {
        public const uint CleanupThreshold = 100;
        public const uint IndexOverflowBuffer = 10000;
    }

    public static class ChainStep
    {
        public const uint DefaultCacheWindowSize = 1000;
        public const uint InitialIndex = 0;
        public const uint IndexIncrement = 1;
        public const uint ResetIndex = 0;
        public const uint MinIndexToKeepOffset = 1;
        public const uint ValidatorArrayEmptyThreshold = 0;
    }

    public static class ProtocolSystem
    {
        public const int EmptyArrayLength = 0;
        public const int MaxIdentityKeyLength = 1024;
        public const int MaxPayloadSize = 10 * 1024 * 1024;
        public const int IntegerOverflowDivisor = 2;
        public const int BufferCopyStartOffset = 0;
        public const int CipherLengthMinimumThreshold = 0;

        public const string DhPublicKeyNullMessage = "DH public key is null";
        public const string NoConnectionMessage = "No connection";
        public const string ReflectionAttackMessage = "Potential reflection attack detected - peer echoed our DH key";
        public const string ParseProtobufFailedMessage = "Failed to parse peer public key bundle from protobuf.";
        public const string SignedPreKeyFailedMessage = "Signed pre-key signature verification failed";
        public const string ProtocolConnectionNotInitializedMessage = "Protocol connection not initialized";
        public const string IdentityKeysTooLargeMessage = "Identity keys too large (max {0} bytes each)";
        public const string IntegerOverflowMessage = "Combined identity keys would cause integer overflow";
        public const string AesGcmEncryptOperationName = "AES-GCM-Encrypt";
        public const string AesGcmDecryptOperationName = "AES-GCM-Decrypt";
        public const string AesGcmEncryptionFailedMessage = "AES-GCM encryption failed.";
        public const string CiphertextTooSmallMessage = "Received ciphertext length ({0}) is smaller than the GCM tag size ({1}).";
        public const string AesGcmDecryptionFailedMessage = "AES-GCM decryption failed (authentication tag mismatch).";
        public const string AesGcmUnexpectedErrorMessage = "Unexpected error during AES-GCM decryption.";
        public const string AdaptiveRatchetNotInitializedMessage = "AdaptiveRatchetManager not initialized";
    }

    public static class Libraries
    {
        public const string LibSodium = "libsodium";
        public const string Kernel32 = "kernel32.dll";
        public const string LibC = "libc";
    }

    public static class KeyPurposes
    {
        public const string FailedToDeriveFormat = "Failed to derive {0} public key.";
        public const string DerivedKeySizeIncorrectFormat = "Derived {0} public key has incorrect size.";
        public const string UnexpectedErrorGeneratingFormat = "Unexpected error generating {0} key pair.";
    }
}