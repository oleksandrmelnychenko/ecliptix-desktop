namespace Ecliptix.Utilities.Failures.Authentication;

public enum AuthenticationFailureType
{
    InvalidCredentials,
    LoginAttemptExceeded,
    MobileNumberRequired,
    SecureKeyRequired,
    UnexpectedError,
    SECURE_MEMORY_ALLOCATION_FAILED,
    SECURE_MEMORY_WRITE_FAILED,
    KeyDerivationFailed,
    MasterKeyDerivationFailed,
    NetworkRequestFailed,
    InvalidMembershipIdentifier,
    HmacKeyGenerationFailed,
    KeySplittingFailed,
    KeyStorageFailed,
    IdentityStorageFailed,
    IdentityNotFound,
    IdentityLoadFailed,
    CriticalAuthenticationError
}
