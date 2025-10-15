namespace Ecliptix.Utilities.Failures.Authentication;

public enum AuthenticationFailureType
{
    InvalidCredentials,
    LoginAttemptExceeded,
    MobileNumberRequired,
    PasswordRequired,
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
