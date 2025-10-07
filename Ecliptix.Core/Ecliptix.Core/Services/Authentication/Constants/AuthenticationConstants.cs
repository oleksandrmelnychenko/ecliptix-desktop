using System;

namespace Ecliptix.Core.Services.Authentication.Constants;

public static class AuthenticationConstants
{
    public const string MobileNumberRequiredKey = "ValidationErrors.MobileNumber.Required";
    public const string MobileNumberIdentifierRequiredKey = "ValidationErrors.MobileNumberIdentifier.Required";
    public const string DeviceIdentifierRequiredKey = "ValidationErrors.DeviceIdentifier.Required";
    public const string SessionIdentifierRequiredKey = "ValidationErrors.SessionIdentifier.Required";
    public const string MembershipIdentifierRequiredKey = "ValidationErrors.MembershipIdentifier.Required";
    public const string SecureKeyRequiredKey = "ValidationErrors.SecureKey.Required";
    public const string InvalidCredentialsKey = "ValidationErrors.SecureKey.InvalidCredentials";
    public const string VerifySecureKeyDoesNotMatchKey = "ValidationErrors.VerifySecureKey.DoesNotMatch";
    public const string CommonUnexpectedErrorKey = "Common.UnexpectedError";

    public const string PasswordStrengthInvalidKey = "ValidationErrors.PasswordStrength.Invalid";
    public const string PasswordStrengthVeryWeakKey = "ValidationErrors.PasswordStrength.VeryWeak";
    public const string PasswordStrengthWeakKey = "ValidationErrors.PasswordStrength.Weak";
    public const string PasswordStrengthGoodKey = "ValidationErrors.PasswordStrength.Good";
    public const string PasswordStrengthStrongKey = "ValidationErrors.PasswordStrength.Strong";
    public const string PasswordStrengthVeryStrongKey = "ValidationErrors.PasswordStrength.VeryStrong";

    public const string NetworkFailurePrefix = "Failed to parse response: ";
    public const string GetResponseFailurePrefix = "Failed to get response: ";
    public const string VerificationFailurePrefix = "Verification failed: ";
    public const string RegistrationFailurePrefix = "Registration failed: ";

    public const string OpaqueInitializationFailureMessage = "Failed to initialize OPAQUE protocol service";

    public const string InvalidOtpCodeKey = "Verification.Error.InvalidOtpCode";
    public const string RegistrationFailedKey = "Registration.Error.Failed";
    public const string NoVerificationSessionKey = "Verification.Error.NoSession";
    public const string VerificationSessionExpiredKey = "Verification.Error.SessionExpired";
    public const string NoActiveVerificationSessionKey = "Verification.Error.NoActiveSession";
    public const string MaxAttemptsReachedKey = "Verification.Error.MaxAttemptsReached";
    public const string SessionNotFoundKey = "Verification.Error.SessionNotFound";
    public const string RedirectingInSecondsKey = "Verification.Info.RedirectingInSeconds";

    public const string AccountAlreadyExistsKey = "ResponseErrors.MobileNumber.AccountAlreadyRegistered";
    public const string UnexpectedMembershipStatusKey = "ResponseErrors.MobileNumber.UnexpectedMembershipStatus"; 

    public const string InitialRemainingTime = "00:30";
    public const string ExpiredRemainingTime = "00:00";

    public static readonly Guid EmptyGuid = Guid.Empty;

    public static class ErrorMessages
    {
        public const string SessionNotFound = "Session not found";
        public const string StartOver = "start over";
        public const string SessionExpiredStartOver = "Session expired. Please start over.";
    }

    public static class Timeouts
    {
        public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan SecrecyChannelRetryDelay = TimeSpan.FromSeconds(1);
    }
}