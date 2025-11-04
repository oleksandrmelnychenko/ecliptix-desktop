namespace Ecliptix.Utilities.Failures.Sodium;

public static class SodiumFailureMessages
{
    public const string SODIUM_INIT_FAILED = "sodium_init() returned an error code.";

    public const string LIBRARY_LOAD_FAILED =
        "Failed to load {0}. Ensure the native library is available and compatible.";

    public const string INITIALIZATION_FAILED = "Failed to initialize libsodium library.";
    public const string UNEXPECTED_INIT_ERROR = "An unexpected error occurred during libsodium initialization.";
    public const string NOT_INITIALIZED = "SodiumInterop is not initialized. Cannot perform secure wipe.";
    public const string BUFFER_NULL = "Buffer cannot be null.";
    public const string BUFFER_TOO_LARGE = "Buffer size ({0:N0} bytes) exceeds maximum ({1:N0} bytes).";
    public const string SMALL_BUFFER_CLEAR_FAILED = "Failed to clear small buffer ({0} bytes) using Array.Clear.";
    public const string PINNING_FAILED = "Failed to pin buffer memory (GCHandle.Alloc). Invalid buffer or handle type.";
    public const string INSUFFICIENT_MEMORY = "Insufficient memory to pin buffer (GCHandle.Alloc).";

    public const string ADDRESS_OF_PINNED_OBJECT_FAILED =
        "GCHandle.Alloc succeeded, but AddrOfPinnedObject returned IntPtr.Zero for a non-empty buffer.";

    public const string GET_PINNED_ADDRESS_FAILED = "Failed to get address of pinned buffer.";
    public const string SECURE_WIPE_FAILED = "Unexpected error during secure wipe via sodium_memzero ({0} bytes).";

    public const string NEGATIVE_ALLOCATION_LENGTH = "Requested allocation length cannot be negative ({0}).";
    public const string SODIUM_NOT_INITIALIZED = "SodiumInterop is not initialized.";
    public const string ALLOCATION_FAILED = "sodium_malloc failed to allocate {0} bytes.";
    public const string UNEXPECTED_ALLOCATION_ERROR = "Unexpected error during allocation ({0} bytes).";
    public const string OBJECT_DISPOSED = "Cannot access disposed resource '{0}'.";
    public const string DATA_TOO_LARGE = "Data length ({0}) exceeds allocated buffer size ({1}).";
    public const string REFERENCE_COUNT_FAILED = "Failed to increment reference count.";
    public const string DISPOSED_AFTER_ADD_REF = "{0} disposed after AddRef.";
    public const string UNEXPECTED_WRITE_ERROR = "Unexpected error during write operation.";
    public const string BUFFER_TOO_SMALL = "Destination buffer size ({0}) is smaller than the allocated size ({1}).";
    public const string UNEXPECTED_READ_ERROR = "Unexpected error during read operation.";
    public const string NEGATIVE_READ_LENGTH = "Requested read length cannot be negative ({0}).";
    public const string READ_LENGTH_EXCEEDS_SIZE = "Requested read length ({0}) exceeds allocated size ({1}).";
    public const string UNEXPECTED_READ_BYTES_ERROR = "Unexpected error reading {0} bytes.";
}
