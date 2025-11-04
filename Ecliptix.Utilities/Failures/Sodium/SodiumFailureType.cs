namespace Ecliptix.Utilities.Failures.Sodium;

public enum SodiumFailureType
{
    INITIALIZATION_FAILED,
    LibraryNotFound,
    ALLOCATION_FAILED,
    MemoryPinningFailed,
    SECURE_WIPE_FAILED,
    InvalidBufferSize,
    BUFFER_TOO_SMALL,
    BUFFER_TOO_LARGE,
    NullPointer,
    MemoryProtectionFailed,
    ComparisonFailed,
    DerivationFailed
}
