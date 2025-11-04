namespace Ecliptix.Utilities;

public enum ERROR_CODE
{
    ValidationFailed = 0,
    MaxAttemptsReached = 1,
    InvalidMobileNumber = 2,
    OTP_EXPIRED = 3,
    NOT_FOUND = 4,
    ALREADY_EXISTS = 5,
    UNAUTHENTICATED = 6,
    PERMISSION_DENIED = 7,
    PRECONDITION_FAILED = 8,
    CONFLICT = 9,
    RESOURCE_EXHAUSTED = 10,
    SERVICE_UNAVAILABLE = 11,
    DEPENDENCY_UNAVAILABLE = 12,
    DEADLINE_EXCEEDED = 13,
    CANCELLED = 14,
    InternalError = 15,
    DATABASE_UNAVAILABLE = 16
}
