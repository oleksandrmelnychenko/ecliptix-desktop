namespace Ecliptix.Utilities.Failures.Authentication;

public enum AuthenticationFailureType
{
    InvalidCredentials,
    LoginAttemptExceeded,
    MobileNumberRequired,
    SecureKeyRequired,
    UnexpectedError,
    SecureMemoryAllocationFailed,
    SecureMemoryWriteFailed,
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
